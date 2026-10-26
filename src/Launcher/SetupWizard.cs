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
        internal SetupWizard(LauncherSettings settings, bool startup, bool firstRun, bool newOptionsOnly) : base(L("Welcome to AO Nexus", "Velkommen til AO Nexus"), 640)
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
            back = Theme.Button(L("Back", "Tilbage")); back.Name = "setupBack";
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
                Text = L("New options in AO Nexus", "Nye muligheder i AO Nexus");
                back.Visible = false;
                SaveButton.Text = L("Save choices", "Gem valg"); CancelAction.Text = L("Later", "Senere");
                Note(L("Choose how to use the new features. Existing choices are preserved. Save once to stop this prompt for these options; Later leaves everything unchanged.",
                    "Vælg, hvordan du vil bruge de nye funktioner. Eksisterende valg bevares. Gem én gang for at undgå denne påmindelse om disse valg; Senere ændrer intet."));
                SetupNewOptions.Add(Fields, draft, original.OptionsReviewVersion);
                Fields.ResumeLayout(true); Theme.Apply(this); Fields.AutoScrollPosition = Point.Empty; QueueContentFit(); return;
            }
            Text = L("AO Nexus setup", "AO Nexus-opsætning");
            back.Text = L("Back", "Tilbage"); back.Visible = step > 0;
            CancelAction.Text = firstRun ? L("Skip setup", "Spring over") : L("Cancel", "Annuller");
            SaveButton.Text = step == 3 ? L("Finish", "Afslut") : L("Next", "Næste");
            var names = new[] { L("Welcome", "Velkommen"), L("Your servers", "Dine servere"), L("App preferences", "Appindstillinger"), L("Review and add accounts", "Gennemse og tilføj konti") };
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
            Note(L("Choose your language, servers and a few preferences. You can change everything later in Settings.", "Vælg sprog, servere og nogle få indstillinger. Alt kan ændres senere i Indstillinger."));
            Note(L("Language", "Sprog"));
            var language = new NexusComboBox { Name = "setupLanguage", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 18) };
            language.Items.AddRange(new object[] { L("Automatic (Windows language)", "Automatisk (Windows-sprog)"), "Dansk", "English" });
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
                ? L("Nothing connects to news, health services or GitHub until you finish. Skip setup leaves those optional features off. No game is started by this wizard.", "Der oprettes ingen forbindelse til nyheder, statustjenester eller GitHub, før du afslutter. Spring over lader disse valgfrie funktioner være slået fra. Guiden starter ikke spillet.")
                : L("Your existing accounts and presets are kept. Changes are saved only when you finish; Cancel leaves your configuration unchanged.", "Dine eksisterende konti og presets bevares. Ændringer gemmes først, når du afslutter; Annuller bevarer din opsætning."));
            var privacy = Theme.Button(L("Privacy and connections…", "Privatliv og forbindelser…")); privacy.Name = "setupPrivacy";
            privacy.Click += (sender, e) => { using (var dialog = new PrivacyDialog()) dialog.ShowDialog(this); };
            Fields.Controls.Add(privacy);
        }

        private void Servers()
        {
            Note(L("Select servers for news and health checks. These choices do not restrict which servers your accounts can use. Custom servers can be added later in Settings.", "Vælg servere til nyheder og statustjek. Valget begrænser ikke, hvilke servere dine konti kan bruge. Egne servere kan tilføjes senere i Indstillinger."));
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
            Check("setupNews", L("Show server news", "Vis servernyheder"), draft.ShowServerNews, value => draft.ShowServerNews = value);
            Check("setupHealth", L("Show server health", "Vis serverstatus"), draft.ShowServerHealth, value => draft.ShowServerHealth = value);
            Note(L("News uses the selected servers' official websites. Health checks contact configured service endpoints and public status reports without logging in. Local/custom servers may have no news source.", "Nyheder hentes fra de valgte serveres officielle hjemmesider. Statustjek kontakter tjenesternes adresser og offentlige statusrapporter uden login. Lokale/egne servere har muligvis ingen nyhedskilde."));
        }

        private void Preferences()
        {
            Check("setupUpdates", L("Check GitHub for updates when Nexus starts", "Tjek GitHub for opdateringer ved start"), draft.CheckForUpdatesOnStartup, value => draft.CheckForUpdatesOnStartup = value);
            Note(L("Checks once at startup. Downloads and installation always require your confirmation.", "Tjekker én gang ved start. Download og installation kræver altid din bekræftelse."));
            Check("setupStartup", L("Start with Windows", "Start med Windows"), startWithWindows, value => startWithWindows = value);
            Note(L("Starts Nexus in the tray. It never logs in game accounts automatically.", "Starter Nexus i bakken. Spilkonti logges aldrig ind automatisk."));
            CheckBox notifications = null;
            var background = Check("setupBackground", L("Check selected servers in the background", "Tjek valgte servere i baggrunden"), draft.BackgroundHealthChecks || draft.HealthTrayNotifications, value =>
            {
                draft.BackgroundHealthChecks = value;
                if (!value) { draft.HealthTrayNotifications = false; if (notifications != null) notifications.Checked = false; }
            });
            notifications = Check("setupNotifications", L("Notify me about outages and recovery", "Giv besked om udfald og genoprettelse"), draft.HealthTrayNotifications, value =>
            {
                draft.HealthTrayNotifications = value; if (value) { draft.BackgroundHealthChecks = true; background.Checked = true; }
            });
            Note(L("Background checks run every 60 seconds while Nexus is open, including in the tray. Notifications enable background checks. Without them, checks run only while the health view is visible.", "Baggrundstjek kører hvert 60. sekund, mens Nexus er åben, også i bakken. Meddelelser aktiverer baggrundstjek. Ellers tjekkes kun, mens statusvisningen er synlig."));
            Note(L("Minimize sends Nexus to the tray; Quit closes it. Both ask for confirmation until you choose not to be asked again.", "Minimer sender Nexus til bakken; Afslut lukker appen. Begge spørger om bekræftelse, indtil du vælger ikke at blive spurgt igen."));
            Note(L("Game clients", "Spilklienter"), true);
            SetupNewOptions.Add(Fields, draft, 0);
        }

        private void Review()
        {
            string on = L("On", "Til"), off = L("Off", "Fra");
            Note(L("Finish saves these preferences. Adding an account is optional; passwords are requested at login.", "Afslut gemmer disse indstillinger. Det er valgfrit at tilføje en konto; adgangskoder indtastes ved login."));
            string servers = string.Join(", ", draft.LoginServers.Where(server => draft.HealthServerIds.Contains(server.Id)).Select(server => server.Name));
            Note(L("Health servers: ", "Statusservere: ") + (servers.Length == 0 ? L("None", "Ingen") : servers)
                + "\r\n" + L("News sources: ", "Nyhedskilder: ") + (draft.NewsSourceIds.Count == 0 ? L("None", "Ingen") : string.Join(", ", ServerNews.Sources.Where(source => draft.NewsSourceIds.Contains(source.Id)).Select(source => source.Name)))
                + "\r\n" + L("News / health view: ", "Nyheder / statusvisning: ") + (draft.ShowServerNews ? on : off) + " / " + (draft.ShowServerHealth ? on : off)
                + "\r\n" + L("Update checks / Windows startup: ", "Opdateringstjek / Windows-start: ") + (draft.CheckForUpdatesOnStartup ? on : off) + " / " + (startWithWindows ? on : off)
                + "\r\n" + L("Background checks / notifications: ", "Baggrundstjek / meddelelser: ") + (draft.BackgroundHealthChecks ? on : off) + " / " + (draft.HealthTrayNotifications ? on : off));
            var accounts = new NexusComboBox { Name = "setupAccounts", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 12) };
            foreach (var option in SetupNewOptions.All) Note(L(option.English, option.Danish) + ": " + (option.Read(draft) ? on : off));
            accounts.MatchHeight(SaveButton);
            foreach (var account in draft.Accounts) accounts.Items.Add(account.Label + " · " + AccountTargets.ServerName(draft, account));
            if (accounts.Items.Count == 0) accounts.Items.Add(L("No accounts added yet", "Ingen konti tilføjet endnu"));
            accounts.SelectedIndex = 0; Fields.Controls.Add(accounts);
            var add = Theme.Button(draft.Accounts.Count == 0 ? L("Add first account…", "Tilføj første konto…") : L("Add another account…", "Tilføj endnu en konto…"));
            add.Name = "setupAddAccount"; add.Click += (sender, e) => AddAccount(); Fields.Controls.Add(add);
            Note(L("Choose the account's server and game folder, then locate its Prefs to import characters. Remembering its password is a per-account choice; saved copies are encrypted for your Windows user.", "Vælg kontoens server og spilmappe, og find dens Prefs for at importere chars. Du vælger pr. konto, om adgangskoden skal huskes; gemte kopier krypteres til din Windows-bruger."));
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
