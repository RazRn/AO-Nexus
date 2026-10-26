using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        public bool MinimizeInactiveClients => minimizeInactiveInput.Checked;
        private readonly CheckBox monitorClientHealthInput, collectClientExitReportsInput;
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
        public Action CheckUpdatesAction { private get; set; }
        public bool CheckForUpdatesOnStartup => checkUpdatesInput.Checked;
        public Func<bool, string> SaveStartupAction { private get; set; }
        public bool StartWithWindows { get => startWithWindowsInput.Checked; set => startWithWindowsInput.Checked = value; }
        public Func<bool> ForgetPasswordsAction { private get; set; }
        public string Language => languageInput.SelectedIndex == 1 ? "da" : languageInput.SelectedIndex == 2 ? "en" : "auto";
        public List<LoginServerProfile> LoginServers { get; private set; }
        public Dictionary<string, HealthEndpoints> HealthCheckEndpoints { get; private set; }
        internal bool ShowServerNews => sidebarOptions.ShowNews;
        internal bool ShowServerHealth => sidebarOptions.ShowHealth;
        internal bool BackgroundHealthChecks => sidebarOptions.BackgroundChecks;
        internal bool HealthTrayNotifications => sidebarOptions.TrayNotifications;
        internal List<string> NewsSourceIds => sidebarOptions.NewsIds;
        internal List<string> HealthServerIds => sidebarOptions.HealthIds;

        public SettingsDialog(LauncherSettings settings) : base(T("Settings"), 580)
        {
            ClientSize = new Size(640, 580);
            HealthCheckEndpoints = HealthEndpoints.Clone(settings.HealthCheckEndpoints);
            LoginServers = settings.LoginServers.Select(item => new LoginServerProfile { Id = item.Id, Name = item.Name, Server = item.Server, Port = item.Port, MaxAccounts = item.MaxAccounts, MaxCharactersPerAccount = item.MaxCharactersPerAccount, CharacterSlots = item.CharacterSlots }).ToList();
            settingsTabs = new NexusTabControl { Name = "settingsTabs", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };
            var general = new TabPage(L("General", "Generelt")) { BackColor = Theme.Background, Padding = new Padding(12) };
            general.Controls.Add(FieldsViewport); FieldsViewport.Dock = DockStyle.Fill;
            settingsTabs.TabPages.Add(general);
            var serverFields = AddPage(L("Accounts", "Konti"));
            var sidebar = new TabPage(L("Sidebar", "Sidepanel")) { BackColor = Theme.Background, Padding = new Padding(12) };
            sidebarOptions = new SidebarOptions(settings); sidebar.Controls.Add(sidebarOptions); settingsTabs.TabPages.Add(sidebar);
            var updateFields = AddPage(L("Updates", "Opdateringer"));
            DialogLayout.Controls.Add(settingsTabs, 0, 0);

            var languagePanel = new Panel { Dock = DockStyle.Top, Height = 69, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 14) };
            languagePanel.Controls.Add(new Label { Text = T("Language"), Dock = DockStyle.Top, Height = 24 });
            languageInput = new NexusComboBox { Dock = DockStyle.Bottom, DropDownStyle = ComboBoxStyle.DropDownList };
            languageInput.Items.AddRange(new object[] { T("Automatic (system language)"), "Dansk", "English" });
            languageInput.SelectedIndex = settings.Language == "da" ? 1 : settings.Language == "en" ? 2 : 0;
            languagePanel.Controls.Add(languageInput); Fields.Controls.Add(languagePanel);
            startWithWindowsInput = Check("Start with Windows", false, "startWithWindows"); Fields.Controls.Add(startWithWindowsInput);
            Note(Fields, T("Start in the system tray when you sign in to Windows. No game accounts are logged in automatically. Turn this off here to remove automatic startup."));
            confirmTrayInput = Check("Confirm before minimizing to tray", !settings.SkipTrayConfirmation, "confirmTray");
            confirmQuitInput = Check("Confirm before quitting Nexus", !settings.SkipQuitConfirmation, "confirmQuit");
            Fields.Controls.Add(confirmTrayInput); Fields.Controls.Add(confirmQuitInput);
            var privacy = Theme.Button(L("Privacy & security…", "Privatliv og sikkerhed…")); privacy.Name = "privacySecurity";
            privacy.UseMnemonic = false;
            privacy.Click += (sender, e) => { using (var dialog = new PrivacyDialog()) dialog.ShowDialog(this); };
            Fields.Controls.Add(privacy);
            var setup = Theme.Button(L("Run setup again…", "Kør opsætning igen…")); setup.Name = "runSetupAgain";
            setup.Click += (sender, e) => { DialogResult = DialogResult.Retry; };
            Fields.Controls.Add(setup);
            var report = Theme.Button(L("Report a problem…", "Rapportér et problem…")); report.Name = "reportProblem";
            report.Click += (sender, e) => ReportProblemAction?.Invoke();
            Fields.Controls.Add(report);
            Note(Fields, L("Creates an anonymised report you can review, then share as a GitHub issue or a file. Nothing is sent automatically.",
                "Laver en anonymiseret rapport, du kan gennemse og dele som en GitHub-sag eller en fil. Intet sendes automatisk."));

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
            var healthEndpoints = Theme.Button(L("Manage health checks…", "Administrer statustjek…"));
            healthEndpoints.Name = "manageHealthChecks";
            healthEndpoints.Click += (sender, e) =>
            {
                using (var dialog = new HealthEndpointsDialog(LoginServers, HealthCheckEndpoints))
                    if (dialog.ShowDialog(this) == DialogResult.OK) HealthCheckEndpoints = dialog.Endpoints;
            };
            serverFields.Controls.Add(healthEndpoints);
            quietLaunchInput = new NexusCheckBox { Name = "quietLaunch", Text = L("Skip login screens (faster)", "Spring login-skærme over (hurtigere)"),
                Checked = settings.QuietLaunch, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            serverFields.Controls.Add(quietLaunchInput);
            Note(serverFields, L("Go straight to the selected character. Login errors and unrecognised screens remain visible. Turn this off to see the normal login screens.",
                "Gå direkte til den valgte char. Login-fejl og ukendte skærme vises stadig. Slå fra for at se de normale login-skærme."));
            fastStartupInput = new NexusCheckBox { Name = "fastStartup", Text = L("Faster game loading", "Hurtigere indlæsning af spillet"),
                Checked = settings.FastStartup, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            serverFields.Controls.Add(fastStartupInput);
            minimizeInactiveInput = new NexusCheckBox { Name = "minimizeInactiveClients", Text = L("Minimize inactive game clients (experimental)", "Minimer inaktive spilklienter (eksperimentel)"),
                Checked = settings.MinimizeInactiveClients, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            // A docked checkbox can retain the pre-scrollbar column width.
            // Bound it to the usable viewport just like the wrapping notes.
            serverFields.ClientSizeChanged += (sender, e) => {
                var width = Math.Max(100, serverFields.ClientSize.Width - 24);
                quietLaunchInput.MaximumSize = new Size(width, 0);
                fastStartupInput.MaximumSize = new Size(width, 0);
                minimizeInactiveInput.MaximumSize = new Size(width, 0);
            };
            Note(serverFields, L("Shortens the logo transitions and initial texture loading on supported classic clients. Other client versions and character creation use normal loading. Turn off if you encounter a loading problem.",
                "Forkorter logo-overgange og den første indlæsning af teksturer på understøttede klassiske klienter. Andre klientversioner og oprettelse af chars bruger normal indlæsning. Slå fra ved problemer med indlæsningen."));
            serverFields.Controls.Add(minimizeInactiveInput);
            Note(serverFields, L("Minimize inactive supported classic clients only when you switch to another AO client managed by Nexus. Switching to a browser, Nexus or another app leaves game windows unchanged. Alt-Tab restores the usual window and graphics. Nexus must stay running.",
                "Minimer kun inaktive understøttede klassiske klienter, når du skifter til en anden AO-klient administreret af Nexus. Skift til en browser, Nexus eller en anden app ændrer ikke spilvinduerne. Alt-Tab gendanner det normale vindue og grafik. Nexus skal fortsat køre."));
            var monitorOption = SetupNewOptions.All.First(o => o.Name == "monitorClientHealth");
            var reportOption = SetupNewOptions.All.First(o => o.Name == "collectClientExitReports");
            monitorClientHealthInput = new NexusCheckBox { Name = monitorOption.Name, Text = L(monitorOption.English, monitorOption.Danish), Checked = settings.MonitorClientHealth, AutoSize = true, Dock = DockStyle.Top };
            collectClientExitReportsInput = new NexusCheckBox { Name = reportOption.Name, Text = L(reportOption.English, reportOption.Danish), Checked = settings.CollectClientExitReports, AutoSize = true, Dock = DockStyle.Top };
            serverFields.Controls.Add(monitorClientHealthInput); Note(serverFields, L(monitorOption.HelpEn, monitorOption.HelpDa));
            serverFields.Controls.Add(collectClientExitReportsInput); Note(serverFields, L(reportOption.HelpEn, reportOption.HelpDa));
            serverFields.ClientSizeChanged += (sender, e) => monitorClientHealthInput.MaximumSize = collectClientExitReportsInput.MaximumSize = new Size(Math.Max(100, serverFields.ClientSize.Width - 24), 0);
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

            checkUpdatesInput = Check("Check for updates when Nexus starts", settings.CheckForUpdatesOnStartup, "checkUpdatesOnStartup");
            updateFields.Controls.Add(checkUpdatesInput);
            Note(updateFields, L("Checks once when Nexus starts. Downloads and installation always need your confirmation.", "Tjekker én gang ved start. Download og installation kræver altid din bekræftelse."));
            var checkUpdates = Theme.Button(T("Check for updates…")); checkUpdates.Name = "checkForUpdates";
            checkUpdates.Click += (sender, e) => CheckUpdatesAction?.Invoke(); updateFields.Controls.Add(checkUpdates);
            var repositoryLink = new LinkLabel { Name = "githubRepository", Text = L("AO Nexus on GitHub", "AO Nexus på GitHub"),
                AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12),
                LinkColor = Theme.Accent, VisitedLinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber,
                LinkBehavior = LinkBehavior.HoverUnderline, TabStop = true };
            repositoryLink.LinkClicked += (sender, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppUpdates.Repository) { UseShellExecute = true }); }
                catch (Exception error) { ErrorLabel.Text = error.Message; }
            };
            updateFields.Controls.Add(repositoryLink);
            Note(updateFields, L("Log size limit (MB)", "Maksimal logstørrelse (MB)"));
            logSizeInput = new NexusNumericUpDown { Name = "maxLogSizeMb", Minimum = 1, Maximum = 100,
                Width = 100, Value = LogRetention.NormalizeMb(settings.MaxLogSizeMb) };
            updateFields.Controls.Add(logSizeInput);
            Note(updateFields, L("Default: 5 MB per log. Oldest entries are removed first. The activity log lasts only for this session; update history is saved on this PC. Update downloads are cleaned up automatically; one previous executable is kept for recovery.",
                "Standard: 5 MB pr. log. Ældste poster fjernes først. Aktivitetsloggen findes kun i denne session; opdateringshistorikken gemmes på denne pc. Opdateringsfiler ryddes automatisk op; én tidligere programfil beholdes til gendannelse."));
            var diagnosticsHeading = Note(updateFields, L("Diagnostics", "Diagnostik"));
            diagnosticsHeading.Font = new Font(settingsTabs.Font, FontStyle.Bold);
            diagnosticsHeading.Margin = new Padding(0, 16, 0, 8);
            keepDiagnosticsInput = new NexusCheckBox { Name = "keepDiagnostics", Text = L("Keep an anonymised diagnostics log on this PC", "Gem en anonymiseret diagnostiklog på denne pc"),
                Checked = settings.KeepDiagnostics, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
            updateFields.Controls.Add(keepDiagnosticsInput);
            Note(updateFields, L("Records errors, login steps and crashes in %LOCALAPPDATA%\\AONexus\\Diagnostics (max 2 MB, plus the 20 newest crash reports). Logins, account and character names, IDs and personal folders are replaced with codes; passwords are never logged. It is only shared when you use Report a problem. Turning it off deletes the saved diagnostics.",
                "Registrerer fejl, login-trin og nedbrud i %LOCALAPPDATA%\\AONexus\\Diagnostics (højst 2 MB samt de 20 nyeste nedbrudsrapporter). Logins, konto- og char-navne, ID'er og personlige mapper erstattes af koder; adgangskoder logges aldrig. Den deles kun, når du bruger Rapportér et problem. Slås den fra, slettes den gemte diagnostik."));
            var clearDiagnostics = Theme.Button(L("Delete diagnostics now", "Slet diagnostik nu")); clearDiagnostics.Name = "clearDiagnostics";
            updateFields.Controls.Add(clearDiagnostics);
            var diagnosticsStatus = Note(updateFields, ""); diagnosticsStatus.Name = "diagnosticsActionStatus"; diagnosticsStatus.Visible = false;
            diagnosticsStatus.TextChanged += (sender, e) => diagnosticsStatus.Visible = !string.IsNullOrEmpty(diagnosticsStatus.Text);
            clearDiagnostics.Click += (sender, e) =>
            {
                if (ClearDiagnosticsAction == null) return;
                bool cleared = ClearDiagnosticsAction();
                diagnosticsStatus.Text = cleared ? L("Saved diagnostics and crash reports were deleted.", "Gemt diagnostik og nedbrudsrapporter er slettet.")
                    : L("Some diagnostics files could not be deleted. Try again.", "Nogle diagnostikfiler kunne ikke slettes. Prøv igen.");
                diagnosticsStatus.ForeColor = cleared ? Theme.Accent : Theme.Warning;
            };
            foreach (var fields in new[] { Fields, serverFields, updateFields })
                foreach (var button in fields.Controls.OfType<Button>())
                {
                    button.Margin = new Padding(0, 0, 0, 8);
                    button.MinimumSize = new Size(220, 34);
                }
            SaveButton.Click += (sender, e) =>
            {
                string startupError = SaveStartupAction?.Invoke(StartWithWindows);
                if (!string.IsNullOrEmpty(startupError)) { ErrorLabel.Text = startupError; return; }
                HealthCheckEndpoints = HealthCheckEndpoints.Where(pair => LoginServers.Any(profile => profile.Id == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
                DialogResult = DialogResult.OK;
            };
            ActiveControl = SaveButton;
        }
        private TableLayoutPanel AddPage(string title)
        {
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, BackColor = Theme.Background };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(12) };
            page.Controls.Add(SlimScrolling.Wrap(fields)); settingsTabs.TabPages.Add(page); return fields;
        }
        private static CheckBox Check(string key, bool value, string name) => new NexusCheckBox { Name = name, Text = T(key), Checked = value, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 8) };
        private static Label Note(TableLayoutPanel fields, string text)
        {
            var note = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(500, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 12) };
            fields.Controls.Add(note); fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0); return note;
        }
    }
}
