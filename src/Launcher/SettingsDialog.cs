using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class SettingsDialog : EditorDialog
    {
        private readonly ComboBox languageInput;
        private readonly NumericUpDown logSizeInput;
        public int MaxLogSizeMb => (int)logSizeInput.Value;
        private readonly CheckBox startWithWindowsInput, checkUpdatesInput, confirmTrayInput, confirmQuitInput, keepDiagnosticsInput, quietLaunchInput, fastStartupInput, minimizeInactiveInput;
        private readonly CheckBox ExperimentalAovkInput;
        public bool ExperimentalAovk => ExperimentalAovkInput.Checked;
        private readonly CheckBox ExperimentalTeamAutomationInput;
        public bool ExperimentalTeamAutomation => ExperimentalTeamAutomationInput.Checked;
        private readonly CheckBox ExperimentalNearbyCombatInput;
        public bool ExperimentalNearbyCombat => ExperimentalNearbyCombatInput.Checked;
        public bool MinimizeInactiveClients => minimizeInactiveInput.Checked;
        private readonly CheckBox monitorClientHealthInput, collectClientExitReportsInput, autoCrashRelogInput;
        public bool AutoRelogCrashedClients => autoCrashRelogInput.Checked;
        private readonly CheckBox confirmLoginInput, confirmTeamLogoutInput;
        public bool MonitorClientHealth => monitorClientHealthInput.Checked;
        public bool CollectClientExitReports => collectClientExitReportsInput.Checked;
        public bool QuietLaunch => quietLaunchInput.Checked;
        public bool FastStartup => fastStartupInput.Checked;
        public bool KeepDiagnostics => keepDiagnosticsInput.Checked;
        public Action ReportProblemAction { private get; set; }
        public Func<bool> ClearDiagnosticsAction { private get; set; }
        internal readonly SidebarOptions sidebarOptions;
        internal readonly NexusTabControl settingsTabs;
        public bool SkipTrayConfirmation => !confirmTrayInput.Checked;
        public bool SkipQuitConfirmation => !confirmQuitInput.Checked;
        public bool SkipLoginConfirmation => !confirmLoginInput.Checked;
        public bool SkipTeamLogoutConfirmation => !confirmTeamLogoutInput.Checked;
        public Action CheckUpdatesAction { private get; set; }
        public bool CheckForUpdatesOnStartup => checkUpdatesInput.Checked;
        public Func<bool, string> SaveStartupAction { private get; set; }
        public bool StartWithWindows { get => startWithWindowsInput.Checked; set => startWithWindowsInput.Checked = value; }
        public Func<bool> ForgetPasswordsAction { private get; set; }
        // Fetches config/servers.json, applies it to the saved settings and returns a status line.
        public Func<Task<string>> UpdateServerRulesAction { private get; set; }
        public bool CheckServerRulesOnStartup => checkServerRulesInput.Checked;
        private readonly CheckBox checkServerRulesInput;
        public Func<bool> GraphicsChangeBlocked { private get; set; }
        public string Language => languageInput.SelectedIndex == 1 ? "da" : languageInput.SelectedIndex == 2 ? "en" : "auto";
        public List<LoginServerProfile> LoginServers { get; private set; }
        public Dictionary<string, HealthEndpoints> HealthCheckEndpoints { get; private set; }
        internal bool ShowServerNews => sidebarOptions.ShowNews;
        internal bool ShowServerHealth => sidebarOptions.ShowHealth;
        internal bool BackgroundHealthChecks => sidebarOptions.BackgroundChecks;
        internal bool HealthTrayNotifications => sidebarOptions.TrayNotifications;
        internal List<string> NewsSourceIds => sidebarOptions.NewsIds;
        internal List<string> HealthServerIds => sidebarOptions.HealthIds;

        internal void ShowGraphics() => settingsTabs.SelectedIndex = 4;
        public SettingsDialog(LauncherSettings settings) : this(settings, null) { }
        public SettingsDialog(LauncherSettings settings, string graphicsFolder) : base(T("Settings"), 580)
        {
            ClientSize = new Size(840, 660);
            FitContentOnOpen = false;
            DialogLayout.Padding = new Padding(16);
            DialogLayout.RowStyles[1].Height = 36;
            HealthCheckEndpoints = HealthEndpoints.Clone(settings.HealthCheckEndpoints);
            LoginServers = settings.LoginServers.Select(ServerRules.Copy).ToList();
            settingsTabs = new NexusTabControl { Name = "settingsTabs", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };
            var general = new TabPage(AOLauncher.Localization.Texts.Get("Text_General_A4EFBDAB2507")) { BackColor = Theme.Background, Padding = new Padding(12) };
            general.Controls.Add(FieldsViewport); FieldsViewport.Dock = DockStyle.Fill;
            ConstrainFieldWidth(Fields);
            settingsTabs.TabPages.Add(general);
            var serverFields = AddPage(Get("Settings.Login"));
            var sidebar = new TabPage(AOLauncher.Localization.Texts.Get("Text_Sidebar_7C11E1F815DF")) { BackColor = Theme.Background, Padding = new Padding(12) };
            sidebarOptions = new SidebarOptions(settings); sidebar.Controls.Add(sidebarOptions); settingsTabs.TabPages.Add(sidebar);
            var updateFields = AddPage(AOLauncher.Localization.Texts.Get("Text_Updates_4FB2C3778125"));
            var graphics = new AovkGraphicsPage(AddPage(AOLauncher.Localization.Texts.Get("Text_Graphics_31F26B3FEEC9")), settings, graphicsFolder);
            graphics.ChangeBlocked = () => GraphicsChangeBlocked?.Invoke() == true;
            var experiments = AddPage(Get("Settings.Advanced"));
            var diagnosticFields = AddPage(Get("Text_Diagnostics_80EEF91F65EE"));
            Section(experiments, Get("Experiments.Title"));
            Note(experiments, Get("Experiments.Description"));
            ExperimentalAovkInput = new NexusCheckBox { Name = "ExperimentalAovk", Text = Get("Experiments.ExperimentalAovk"), Checked = settings.ExperimentalAovk, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10) };
            experiments.Controls.Add(ExperimentalAovkInput);
            ExperimentalTeamAutomationInput = new NexusCheckBox { Name = "ExperimentalTeamAutomation", Text = Get("Experiments.ExperimentalTeamAutomation"), Checked = settings.ExperimentalTeamAutomation, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10) };
            experiments.Controls.Add(ExperimentalTeamAutomationInput);
            ExperimentalNearbyCombatInput = new NexusCheckBox { Name = "ExperimentalNearbyCombat", Text = Get("Experiments.ExperimentalNearbyCombat"), Checked = settings.ExperimentalNearbyCombat, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 10) };
            experiments.Controls.Add(ExperimentalNearbyCombatInput);
            Note(experiments, Get("Experiments.ExistingFiles"));
            Note(experiments, Get("Experiments.TimerOptIn"));
            graphics.ExperimentEnabled = () => ExperimentalAovk;
            ExperimentalAovkInput.CheckedChanged += (sender, e) => graphics.RefreshAvailability();
            graphics.RefreshAvailability();
            DialogLayout.Controls.Add(settingsTabs, 0, 0);

            Section(Fields, Get("Settings.Application"));
            var languagePanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            languagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            languagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            languagePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            languagePanel.Controls.Add(new Label { Text = T("Language"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty });
            languageInput = new NexusComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDownList, Margin = Padding.Empty };
            languageInput.Items.AddRange(new object[] { T("Automatic (system language)"), "Dansk", "English" });
            languageInput.SelectedIndex = settings.Language == "da" ? 1 : settings.Language == "en" ? 2 : 0;
            languagePanel.Controls.Add(languageInput); Fields.Controls.Add(languagePanel);
            startWithWindowsInput = Check("Start with Windows", false, "startWithWindows"); Fields.Controls.Add(startWithWindowsInput);
            Note(Fields, Get("Settings.StartupHelp"));
            Section(Fields, Get("Settings.Confirmations"));
            confirmTrayInput = Check("Confirm before minimizing to tray", !settings.SkipTrayConfirmation, "confirmTray");
            confirmQuitInput = Check("Confirm before quitting Nexus", !settings.SkipQuitConfirmation, "confirmQuit");
            Fields.Controls.Add(confirmTrayInput); Fields.Controls.Add(confirmQuitInput);
            confirmLoginInput = new NexusCheckBox { Name = "confirmLogin", Text = AOLauncher.Localization.Texts.Get("Text_Confirm_before_logging_in_98A65467DE1D"),
                Checked = !settings.SkipLoginConfirmation, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            confirmTeamLogoutInput = new NexusCheckBox { Name = "confirmTeamLogout", Text = AOLauncher.Localization.Texts.Get("Text_Confirm_before_team_logout_8802F23DE4EB"),
                Checked = !settings.SkipTeamLogoutConfirmation, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            Fields.Controls.Add(confirmLoginInput); Fields.Controls.Add(confirmTeamLogoutInput);
            Section(Fields, Get("Settings.Tools"));
            var privacy = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Privacy_security_6BB81ABE3527")); privacy.Name = "privacySecurity";
            privacy.UseMnemonic = false;
            privacy.Click += (sender, e) => { using (var dialog = new PrivacyDialog()) dialog.ShowDialog(this); };
            Fields.Controls.Add(privacy);
            var setup = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Run_setup_again_DB8AC0F0BE6E")); setup.Name = "runSetupAgain";
            setup.Click += (sender, e) => { DialogResult = DialogResult.Retry; };
            Fields.Controls.Add(setup);
            var report = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Report_a_problem_69BD52D2AC83")); report.Name = "reportProblem";
            report.Click += (sender, e) => ReportProblemAction?.Invoke();
            ButtonRow(Fields, privacy, setup);

            Section(serverFields, Get("Settings.Launching"));
            autoCrashRelogInput = new NexusCheckBox { Name = "autoRelogCrashedClients", Text = Get("CrashRelog.Option"), Checked = settings.AutoRelogCrashedClients, AutoSize = true, Dock = DockStyle.Top };
            serverFields.Controls.Add(autoCrashRelogInput);
            Note(serverFields, Get("CrashRelog.Help"));

            quietLaunchInput = new NexusCheckBox { Name = "quietLaunch", Text = AOLauncher.Localization.Texts.Get("Text_Skip_login_screens_faster_F502C8C9FEED"),
                Checked = settings.QuietLaunch, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            serverFields.Controls.Add(quietLaunchInput);
            Note(serverFields, AOLauncher.Localization.Texts.Get("Text_Go_straight_to_the_selected_character_305C40B97F45"));
            fastStartupInput = new NexusCheckBox { Name = "fastStartup", Text = AOLauncher.Localization.Texts.Get("Text_Faster_game_loading_92191C917B5A"),
                Checked = settings.FastStartup, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            serverFields.Controls.Add(fastStartupInput);
            minimizeInactiveInput = new NexusCheckBox { Name = "minimizeInactiveClients", Text = AOLauncher.Localization.Texts.Get("Text_Minimize_inactive_game_clients_experimental_061172F85149"),
                Checked = settings.MinimizeInactiveClients, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            // A docked checkbox can retain the pre-scrollbar column width.
            // Bound it to the usable viewport just like the wrapping notes.
            serverFields.ClientSizeChanged += (sender, e) => {
                var width = Math.Max(100, serverFields.ClientSize.Width - 24);
                quietLaunchInput.MaximumSize = new Size(width, 0);
                fastStartupInput.MaximumSize = new Size(width, 0);
            };
            Note(serverFields, AOLauncher.Localization.Texts.Get("Text_Shortens_the_logo_transitions_and_initial_9C629491CE73"));
            experiments.ClientSizeChanged += (sender, e) => minimizeInactiveInput.MaximumSize = new Size(Math.Max(100, experiments.ClientSize.Width - 24), 0);
            Section(experiments, Get("Settings.BackgroundClients"));
            experiments.Controls.Add(minimizeInactiveInput);
            Note(experiments, AOLauncher.Localization.Texts.Get("Text_Supports_the_tested_classic_renderer_and_76EF5D0008D1"));
            Section(serverFields, Get("Settings.Servers"));
            Note(serverFields, T("Set the game folder and login server under each account: Edit → Game and server."));
            var serversButton = Theme.Button(T("Manage login servers…")); serversButton.Name = "manageLoginServers";
            serversButton.Click += (sender, e) =>
            {
                using (var dialog = new LoginServersDialog(LoginServers))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        var selected = sidebarOptions.HealthIds; LoginServers = dialog.LoginServers;
                        sidebarOptions.RebuildServers(LoginServers, selected);
                    }
            };
            serverFields.Controls.Add(serversButton);
            var healthEndpoints = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Manage_health_checks_43E6CD44841A"));
            healthEndpoints.Name = "manageHealthChecks";
            healthEndpoints.Click += (sender, e) =>
            {
                using (var dialog = new HealthEndpointsDialog(LoginServers, HealthCheckEndpoints))
                    if (dialog.ShowDialog(this) == DialogResult.OK) HealthCheckEndpoints = dialog.Endpoints;
            };
            serverFields.Controls.Add(healthEndpoints);
            ButtonRow(serverFields, serversButton, healthEndpoints);
            var monitorOption = SetupNewOptions.All.First(o => o.Name == "monitorClientHealth");
            var reportOption = SetupNewOptions.All.First(o => o.Name == "collectClientExitReports");
            monitorClientHealthInput = new NexusCheckBox { Name = monitorOption.Name, Text = AOLauncher.Localization.Texts.Get(monitorOption.TextKey), Checked = settings.MonitorClientHealth, AutoSize = true, Dock = DockStyle.Top };
            collectClientExitReportsInput = new NexusCheckBox { Name = reportOption.Name, Text = AOLauncher.Localization.Texts.Get(reportOption.TextKey), Checked = settings.CollectClientExitReports, AutoSize = true, Dock = DockStyle.Top };
            Section(diagnosticFields, Get("Settings.ClientMonitoring"));
            diagnosticFields.Controls.Add(monitorClientHealthInput); Note(diagnosticFields, AOLauncher.Localization.Texts.Get(monitorOption.HelpKey));
            diagnosticFields.Controls.Add(collectClientExitReportsInput); Note(diagnosticFields, AOLauncher.Localization.Texts.Get(reportOption.HelpKey));
            diagnosticFields.ClientSizeChanged += (sender, e) => monitorClientHealthInput.MaximumSize = collectClientExitReportsInput.MaximumSize = new Size(Math.Max(100, diagnosticFields.ClientSize.Width - 24), 0);
            var passwordsHeading = Note(serverFields, T("Saved passwords"));
            passwordsHeading.Font = new Font(settingsTabs.Font, FontStyle.Bold);
            passwordsHeading.Margin = new Padding(0, 16, 0, 8);
            Note(serverFields, T("Clear saved passwords for all AO Nexus accounts immediately. You will be asked again at the next login."));
            var forget = Theme.Button(T("Forget passwords")); forget.Name = "forgetSavedPasswords";
            serverFields.Controls.Add(forget);
            var passwordStatus = Note(serverFields, ""); passwordStatus.Name = "passwordActionStatus";
            passwordStatus.Visible = false;
            passwordStatus.TextChanged += (sender, e) => {
                passwordStatus.Visible = !string.IsNullOrEmpty(passwordStatus.Text);
                QueueContentFit();
            };
            forget.Click += (sender, e) =>
            {
                if (ForgetPasswordsAction == null) return;
                bool cleared = ForgetPasswordsAction();
                passwordStatus.Text = cleared ? T("Saved passwords cleared. Accounts and presets are unchanged.") : T("Passwords could not be cleared. Try again.");
                passwordStatus.ForeColor = cleared ? Theme.Accent : Theme.Warning;
            };

            Section(updateFields, Get("Settings.NexusUpdates"));
            checkUpdatesInput = Check("Check for updates when Nexus starts", settings.CheckForUpdatesOnStartup, "checkUpdatesOnStartup");
            updateFields.Controls.Add(checkUpdatesInput);
            Note(updateFields, AOLauncher.Localization.Texts.Get("Text_Checks_once_when_Nexus_starts_Downloads_AB8783706E92"));
            var checkUpdates = Theme.Button(T("Check for updates…")); checkUpdates.Name = "checkForUpdates";
            checkUpdates.Click += (sender, e) => CheckUpdatesAction?.Invoke(); updateFields.Controls.Add(checkUpdates);
            var repositoryLink = new LinkLabel { Name = "githubRepository", Text = AOLauncher.Localization.Texts.Get("Text_AO_Nexus_on_GitHub_D597B75EA496"),
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12),
                LinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber,
                LinkBehavior = LinkBehavior.HoverUnderline, TabStop = true };
            repositoryLink.LinkClicked += (sender, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppUpdates.Repository) { UseShellExecute = true }); }
                catch (Exception error) { ErrorLabel.Text = error.Message; }
            };
            updateFields.Controls.Add(repositoryLink);
            var rulesHeading = Note(updateFields, AOLauncher.Localization.Texts.Get("Text_Server_rules_F5A3AA9E146B"));
            rulesHeading.Font = new Font(settingsTabs.Font, FontStyle.Bold); rulesHeading.ForeColor = Theme.Foreground; rulesHeading.Margin = new Padding(0, 12, 0, 6);
            Note(updateFields, AOLauncher.Localization.Texts.Get("Text_Login_addresses_ports_accounts_and_chars_76D4597C78DC"));
            var rulesStatus = Note(updateFields, string.Format(AOLauncher.Localization.Texts.Get("Text_Revision_0_source_1_4BAECE779336"), Math.Max(settings.ServerRulesRevision, ServerRules.Current.Revision), ServerRules.DescribeSource(settings)));
            rulesStatus.Name = "serverRulesStatus";
            var updateRules = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Update_server_rules_and_IP_port_23E5E52DCE7F")); updateRules.Name = "updateServerRules";
            updateRules.Click += async (sender, e) =>
            {
                if (UpdateServerRulesAction == null) return;
                updateRules.Enabled = false; rulesStatus.ForeColor = Theme.Muted; rulesStatus.Text = AOLauncher.Localization.Texts.Get("Text_Checking_the_server_list_AF446B2D3709");
                try
                {
                    rulesStatus.Text = await UpdateServerRulesAction();
                    // The update was saved; show it here so Save does not write back the old list.
                    LoginServers = settings.LoginServers.Select(ServerRules.Copy).ToList();
                    sidebarOptions.RebuildServers(LoginServers, sidebarOptions.HealthIds);
                    rulesStatus.ForeColor = Theme.Accent;
                }
                catch (Exception ex) { rulesStatus.ForeColor = Theme.Warning; rulesStatus.Text = AOLauncher.Localization.Texts.Get("Text_Could_not_update_47E522E3562B") + ex.Message; }
                finally { updateRules.Enabled = true; QueueContentFit(); }
            };
            updateFields.Controls.Add(updateRules);
            checkServerRulesInput = new NexusCheckBox { Name = "checkServerRulesOnStartup", Text = AOLauncher.Localization.Texts.Get("Text_Check_for_new_server_rules_when_9A4DAB84845A"),
                Checked = settings.CheckServerRulesOnStartup, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            updateFields.Controls.Add(checkServerRulesInput);
            Section(diagnosticFields, Get("Settings.LocalLogs"));
            Note(diagnosticFields, AOLauncher.Localization.Texts.Get("Text_Log_size_limit_MB_DE35A68F4483"));
            logSizeInput = new NexusNumericUpDown { Name = "maxLogSizeMb", Minimum = 1, Maximum = 100,
                Width = 100, Value = LogRetention.NormalizeMb(settings.MaxLogSizeMb) };
            diagnosticFields.Controls.Add(logSizeInput);
            Note(diagnosticFields, AOLauncher.Localization.Texts.Get("Text_Default_5_MB_per_log_Oldest_49EA05F515AF"));
            keepDiagnosticsInput = new NexusCheckBox { Name = "keepDiagnostics", Text = AOLauncher.Localization.Texts.Get("Text_Keep_an_anonymised_diagnostics_log_on_D5B365BB3DD5"),
                Checked = settings.KeepDiagnostics, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            diagnosticFields.Controls.Add(keepDiagnosticsInput);
            Note(diagnosticFields, AOLauncher.Localization.Texts.Get("Text_Records_errors_login_steps_and_crashes_760666D1B427"));
            var clearDiagnostics = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Delete_diagnostics_now_EAC680B9A1AE")); clearDiagnostics.Name = "clearDiagnostics";
            ButtonRow(diagnosticFields, report, clearDiagnostics);
            var diagnosticsStatus = Note(diagnosticFields, ""); diagnosticsStatus.Name = "diagnosticsActionStatus"; diagnosticsStatus.Visible = false;
            diagnosticsStatus.TextChanged += (sender, e) => diagnosticsStatus.Visible = !string.IsNullOrEmpty(diagnosticsStatus.Text);
            clearDiagnostics.Click += (sender, e) =>
            {
                if (ClearDiagnosticsAction == null) return;
                bool cleared = ClearDiagnosticsAction();
                diagnosticsStatus.Text = cleared ? AOLauncher.Localization.Texts.Get("Text_Saved_diagnostics_and_crash_reports_were_A147A873D6F9")
                    : AOLauncher.Localization.Texts.Get("Text_Some_diagnostics_files_could_not_be_36CD8686EB78");
                diagnosticsStatus.ForeColor = cleared ? Theme.Accent : Theme.Warning;
            };
            foreach (var fields in new[] { Fields, serverFields, updateFields, diagnosticFields, experiments })
                foreach (var button in fields.Controls.OfType<Button>())
                {
                    button.Margin = new Padding(0, 0, 0, 8);
                    button.MinimumSize = new Size(220, 34);
                }
            var recommended = Theme.Button(Get("Settings.Recommended"));
            recommended.Name = "recommendedSettings";
            recommended.AutoSize = true;
            recommended.MinimumSize = new Size(220, 34);
            var actions = (FlowLayoutPanel)SaveButton.Parent;
            actions.Controls.Add(recommended);
            var tips = new ToolTip { AutoPopDelay = 20000 };
            tips.SetToolTip(recommended, Get("Settings.RecommendedHelp"));
            recommended.Click += (sender, e) => ApplyRecommendedSettings();
            Disposed += (sender, e) => tips.Dispose();
            Section(Fields, Get("Settings.Recommended"));
            Note(Fields, Get("Settings.RecommendedHelp"));
            SaveButton.Click += (sender, e) =>
            {
                ErrorLabel.ForeColor = Theme.Warning;
                ErrorLabel.Text = "";
                string startupError = SaveStartupAction?.Invoke(StartWithWindows);
                if (!string.IsNullOrEmpty(startupError)) { ErrorLabel.Text = startupError; return; }
                try
                {
                    if (graphics.HasChanges && GraphicsChangeBlocked?.Invoke() == true)
                        throw new System.IO.IOException(AOLauncher.Localization.Texts.Get("Text_Wait_for_the_current_launch_or_D4C3783837F0"));
                    if (!graphics.PrepareDownload(this)) return;
                    graphics.Save();
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is AggregateException)
                {
                    FailureDiagnostics.Install.Report("save-graphics", "Graphics changes could not be applied", ex); ErrorLabel.Text = ex.Message; return; }
                HealthCheckEndpoints = HealthCheckEndpoints.Where(pair => LoginServers.Any(profile => profile.Id == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                DialogResult = DialogResult.OK;
            };
            ActiveControl = SaveButton;
        }
        // This changes only the dialog draft. Save owns all persistence and side effects.
        internal void ApplyRecommendedSettings()
        {
            quietLaunchInput.Checked = true;
            fastStartupInput.Checked = true;
            monitorClientHealthInput.Checked = true;
            collectClientExitReportsInput.Checked = true;
            keepDiagnosticsInput.Checked = true;
            logSizeInput.Value = 5;
            checkUpdatesInput.Checked = true;
            checkServerRulesInput.Checked = true;
            confirmTrayInput.Checked = true;
            confirmQuitInput.Checked = true;
            confirmLoginInput.Checked = true;
            confirmTeamLogoutInput.Checked = true;
            ErrorLabel.ForeColor = Theme.Accent;
            ErrorLabel.Text = Get("Settings.RecommendedStaged");
        }

        private static void Section(TableLayoutPanel fields, string text)
        {
            var label = Note(fields, text);
            label.Font = new Font(fields.Font, FontStyle.Bold);
            label.ForeColor = Theme.Accent;
            label.Margin = new Padding(0, fields.Controls.Count == 1 ? 0 : 14, 0, 8);
        }

        private static void ButtonRow(TableLayoutPanel fields, params Button[] buttons)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            foreach (var button in buttons)
            {
                button.AutoSize = true;
                button.MinimumSize = new Size(180, 34);
                button.Margin = new Padding(0, 0, 8, 0);
                row.Controls.Add(button);
            }
            fields.Controls.Add(row);
            fields.ClientSizeChanged += (sender, e) => row.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0);
        }

        private TableLayoutPanel AddPage(string title)
        {
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Background, Margin = Padding.Empty };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ConstrainFieldWidth(fields);
            var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background, Margin = Padding.Empty };
            viewport.Controls.Add(fields);
            viewport.ClientSizeChanged += (sender, e) => {
                fields.MaximumSize = new Size(Math.Max(100, viewport.ClientSize.Width - 12), 0);
            };
            var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(12) };
            page.Controls.Add(SlimScrolling.Wrap(viewport)); settingsTabs.TabPages.Add(page); return fields;
        }
        private static CheckBox Check(string key, bool value, string name) => new NexusCheckBox { Name = name, Text = T(key), Checked = value, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
        private static void ConstrainFieldWidth(TableLayoutPanel fields)
        {
            // Nested auto-sized tables otherwise retain their old preferred width
            // when a vertical scrollbar appears, creating horizontal overflow.
            fields.ClientSizeChanged += (sender, e) => {
                int width = Math.Max(100, fields.ClientSize.Width - 24);
                fields.ColumnStyles[0].SizeType = SizeType.Absolute;
                fields.ColumnStyles[0].Width = width;
                foreach (Control child in fields.Controls)
                    child.MaximumSize = new Size(width, child.MaximumSize.Height);
            };
        }
        private static Label Note(TableLayoutPanel fields, string text)
        {
            var note = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(500, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 8) };
            fields.Controls.Add(note); fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0); return note;
        }
    }
}
