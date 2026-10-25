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
        private readonly CheckBox startWithWindowsInput, checkUpdatesInput, confirmTrayInput, confirmQuitInput, keepDiagnosticsInput, quietLaunchInput, overlapLoginsInput;
        public bool QuietLaunch => quietLaunchInput.Checked;
        public bool OverlapLogins => overlapLoginsInput.Checked;
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
            quietLaunchInput = new NexusCheckBox { Name = "quietLaunch", Text = L("Skip drawing the login screens (faster)", "Spring tegning af login-skærmene over (hurtigere)"),
                Checked = settings.QuietLaunch, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            serverFields.Controls.Add(quietLaunchInput);
            Note(serverFields, L("On by default. The game client goes straight to the selected character without showing the login and character list, about 1.5 seconds faster per character. Login errors are then not shown in the game window; the activity log and Report a problem show where a login stopped. Turn it off to see the game's own login screens.",
                "Slået til som standard. Spilklienten går direkte til den valgte char uden at vise login og char-liste, ca. 1,5 sekund hurtigere pr. char. Login-fejl vises så ikke i spilvinduet; aktivitetsloggen og Rapportér et problem viser, hvor et login stoppede. Slå den fra for at se spillets egne login-skærme."));
            overlapLoginsInput = new NexusCheckBox { Name = "overlapLogins", Text = L("Overlap logins on one account (experimental)", "Overlappende logins på én konto (eksperimentel)"),
                Checked = settings.OverlapLogins, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 8) };
            serverFields.Controls.Add(overlapLoginsInput);
            Note(serverFields, L("Characters on the same account normally start one by one after the previous one is in the world. With this on, the next starts while the previous one is still loading, so a team logs in much faster. If the server rejects overlapping logins, turn it off again.",
                "Chars på samme konto starter normalt én ad gangen, når den forrige står i verden. Med dette slået til starter den næste, mens den forrige stadig indlæser, så et hold logger meget hurtigere ind. Hvis serveren afviser overlappende logins, så slå det fra igen."));
            var passwordsHeading = Note(serverFields, T("Saved passwords"));
            passwordsHeading.Font = new Font(settingsTabs.Font, FontStyle.Bold);
            passwordsHeading.Margin = new Padding(0, 16, 0, 8);
            Note(serverFields, T("Clear saved passwords for all AO Nexus accounts immediately. You will be asked again at the next login."));
            var forget = Theme.Button(T("Forget passwords")); forget.Name = "forgetSavedPasswords";
            serverFields.Controls.Add(forget);
            var passwordStatus = Note(serverFields, ""); passwordStatus.Name = "passwordActionStatus";
            passwordStatus.Visible = false;
            passwordStatus.TextChanged += (sender, e) => passwordStatus.Visible = !string.IsNullOrEmpty(passwordStatus.Text);
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
