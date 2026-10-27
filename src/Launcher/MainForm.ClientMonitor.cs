using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        ClientMonitor clientMonitor;
        readonly Timer clientMonitorTimer = new Timer { Interval = 1000 };
        readonly ToolTip clientTooltip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100, ShowAlways = true };
        LinkLabel clientStatus;
        ToolStripMenuItem trayClientsItem;
        List<ClientObservation> clientObservations = new List<ClientObservation>();
        string clientReport = "{}", clientObserverError;
        bool clientPollBusy, clientMonitorDisposed;
        DateTime exitsSeenUtc = DateTime.MinValue;

        void InitializeClientStatus()
        {
            clientStatus = new LinkLabel { Name = "clientHealth", Visible = false, Location = new Point(109, 99), Size = new Size(500, 20),
                Font = new Font("Segoe UI", 9F), BackColor = Color.Transparent, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber,
                LinkBehavior = LinkBehavior.HoverUnderline, AccessibleName = L("Character status", "Karakterstatus") };
            clientStatus.LinkClicked += (sender, args) => ShowClientStatus();
            header.Controls.Add(clientStatus);
            trayClientsItem = new ToolStripMenuItem(L("Characters · status and help", "Karakterer · status og hjælp"));
            trayClientsItem.Click += (sender, args) => BeginInvoke((Action)ShowClientStatus);
            trayMenu.Items.Insert(2, trayClientsItem);
            if (smoke || storageBlocked) return;
            clientMonitor = new ClientMonitor();
            clientMonitorTimer.Tick += async (sender, args) => await PollClients();
            clientMonitorTimer.Start();
        }
        async Task PollClients()
        {
            if (clientPollBusy || clientMonitorDisposed || closing) return;
            clientPollBusy = true;
            try {
                if (!settings.MonitorClientHealth && !settings.CollectClientExitReports) {
                    clientMonitor?.Dispose(); clientMonitor = null;
                    clientObservations.Clear(); clientObserverError = null; clientReport = "{}";
                    RefreshClientStatus(); RefreshHealthTray(); return;
                }
                if (clientMonitor == null) clientMonitor = new ClientMonitor();
                // Snapshot the mutable UI model before doing discovery/file reads off the UI thread.
                var serializer = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                var snapshot = serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
                var monitor = clientMonitor;
                await Task.Run(() => monitor.Poll(snapshot));
                if (clientMonitorDisposed || closing || IsDisposed) return;
                clientObservations = monitor.Current;
                clientReport = monitor.Capture(); clientObserverError = monitor.Error;
                foreach (var change in monitor.Changes) AppendLog(change);
                RefreshClientStatus(); RefreshHealthTray();
            } catch (Exception error) {
                clientObserverError = error.GetType().Name;
                // A failed observer must never leave a green indicator on screen.
                foreach (var row in clientObservations.Where(c => c.Measured && !c.ExitedUtc.HasValue)) row.Condition = ClientCondition.MeasurementUnavailable;
                if (!clientMonitorDisposed && !IsDisposed) { RefreshClientStatus(); RefreshHealthTray(); }
            } finally {
                clientPollBusy = false;
                if (clientMonitorDisposed) { clientMonitor?.Dispose(); clientMonitor = null; }
            }
        }
        IEnumerable<ClientObservation> MeasuredClients => clientObservations.Where(c => c.Measured && !c.ExitedUtc.HasValue);
        bool ClientWarning => MeasuredClients.Any(c => c.Condition == ClientCondition.Delayed || c.Condition == ClientCondition.NoResponse || c.Condition == ClientCondition.MeasurementUnavailable || c.HasCastIssue) || clientObserverError != null && MeasuredClients.Any();
        bool NewClientExit => clientObservations.Any(c => c.ExitedUtc.HasValue && c.ExitedUtc.Value > exitsSeenUtc);
        string ClientSummary()
        {
            var rows = MeasuredClients.ToArray();
            if (rows.Length == 0) {
                if (NewClientExit) return L("Characters · game closed · view details", "Karakterer · spil lukket · se detaljer");
                if (settings.MonitorClientHealth && clientObservations.Any(c => !c.ExitedUtc.HasValue)) {
                    var live = clientObservations.Where(c => !c.ExitedUtc.HasValue).ToArray();
                    int stopped = live.Count(c => c.Condition == ClientCondition.Unloaded);
                    if (stopped == live.Length)
                        return L("Characters · monitoring stopped · click for help", "Karakterer · overvågning stoppet · klik for hjælp");
                    if (stopped > 0)
                        return L("Characters · ", "Karakterer · ") + stopped + L(" monitoring stopped", " overvågning stoppet")
                            + " · " + (live.Length - stopped) + L(" not measured", " ikke målt");
                    return L("Characters · monitoring not set up · click for help", "Karakterer · overvågning ikke opsat · klik for hjælp");
                }
                return null;
            }
            var bad = rows.FirstOrDefault(c => c.Condition == ClientCondition.NoResponse) ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.Delayed)
                ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.MeasurementUnavailable);
            if (bad != null) return bad.Name + " · " + ClientStatusPresentation.For(bad, clientObserverError).Status;
            if (clientObserverError != null) return L("Characters · measurement unavailable", "Karakterer · måling utilgængelig");
            var actionIssue = rows.FirstOrDefault(c => c.HasCastIssue);
            if (actionIssue != null) return actionIssue.Name + L(" · recent cast issue: falling", " · nyligt kasteproblem: fald");
            int healthy = rows.Count(c => c.Condition == ClientCondition.Healthy);
            int unmeasured = clientObservations.Count(c => !c.ExitedUtc.HasValue && !c.Measured);
            return L("Characters · ", "Karakterer · ") + healthy + "/" + rows.Length + L(" responding", " svarer") + (unmeasured == 0 ? "" : " · " + unmeasured + L(" not measured", " ikke målt"));
        }
        internal static string LimitTrayText(string text)
        {
            if (text.Length <= 63) return text;
            int length = char.IsHighSurrogate(text[61]) ? 61 : 62;
            return text.Substring(0, length) + "…";
        }
        void RefreshClientStatus()
        {
            if (clientStatus == null) return;
            clientStatus.AccessibleName = L("Character status", "Karakterstatus");
            if (trayClientsItem != null) trayClientsItem.Text = L("Characters · status and help", "Karakterer · status og hjælp");
            string summary = ClientSummary();
            clientStatus.Visible = summary != null;
            if (summary != null) clientStatus.Text = summary;
            clientStatus.LinkColor = ClientWarning ? Theme.Warning : MeasuredClients.Any() ? Theme.Accent : Theme.Muted;
            string details = string.Join("\r\n\r\n", clientObservations.Select(row => {
                var state = ClientStatusPresentation.For(row, clientObserverError);
                return row.Name + " · " + state.Status + "\r\n" + state.Explanation;
            }));
            clientTooltip.SetToolTip(clientStatus, details.Length > 3500 ? details.Substring(0, 3500) + "…" : details);
        }
        string ClientDetails()
        {
            var result = new StringBuilder();
            foreach (var row in clientObservations) {
                result.Append(row.Name).Append(" · ").Append(ClientMonitor.ConditionText(row.Condition));
                if (row.ExitedUtc.HasValue) result.Append(" · ").Append(row.ExitedUtc.Value.ToLocalTime().ToString("HH:mm:ss"))
                    .Append(" · exit ").Append(row.ExitCode?.ToString() ?? "?");
                else if (row.Measured && row.Sample != null) {
                    var s = row.Sample;
                    result.AppendFormat(L(" · IPC {0} ms · {1} timeouts · {2} late", " · IPC {0} ms · {1} timeouts · {2} sene svar"), s.ReplyMs?.ToString("F1") ?? "?", s.Timeouts?.ToString() ?? "?", s.Late?.ToString() ?? "?");
                    result.AppendFormat("\r\n  {0}: {1} ms · max/10s: {2} ms · max: {3} ms", L("Update gap at snapshot", "Opdateringspause ved måling"),
                        s.ObservedUpdateGapMs?.ToString("F0") ?? "?", s.RecentMaxGapMs?.ToString("F1") ?? "?", s.MaxGapMs?.ToString("F1") ?? "?");
                    result.AppendFormat(L("\r\n  Snapshot age: {0} ms · helper age: {1} ms · reply age: {2} ms", "\r\n  Målingens alder: {0} ms · hjælperens alder: {1} ms · svarets alder: {2} ms"),
                        s.PulseAgeMs?.ToString("F0") ?? "?", s.HelperAgeMs?.ToString("F0") ?? "?", s.ReplyAgeMs?.ToString("F0") ?? "?");
                    if (s.Reason != null) result.Append("\r\n  ").Append(s.Reason);
                    if (s.Minimized) result.Append(L(" · minimized", " · minimeret"));
                    if (s.Zoning) result.Append(L(" · zoning (last observed)", " · zoning (senest observeret)"));
                }
                result.AppendLine();
            }
            if (result.Length == 0) result.AppendLine(L("No Nexus-managed clients detected.", "Ingen Nexus-styrede klienter fundet."));
            if (clientObserverError != null) result.AppendLine(L("Measurement: ", "Måling: ") + clientObserverError);
            return result.ToString();
        }
        void ShowClientStatus()
        {
            ShowLauncher(); exitsSeenUtc = DateTime.UtcNow; RefreshClientStatus(); RefreshHealthTray();
            using (var dialog = new ClientStatusDialog(() => ClientDetails(), () => clientReport, () => clientObservations.ToArray(), () => clientObserverError)) dialog.ShowDialog(this);
        }
        void DisposeClientMonitor()
        {
            clientMonitorDisposed = true; clientMonitorTimer.Stop(); clientMonitorTimer.Dispose(); clientTooltip.Dispose();
            if (!clientPollBusy) { clientMonitor?.Dispose(); clientMonitor = null; }
        }
    }
    internal sealed class ClientStatusPresentation
    {
        internal string Status, Explanation, NextStep;
        internal string ActionStatus;
        internal Color Color;

        internal static ClientStatusPresentation For(ClientObservation row, string observerError)
        {
            var condition = row.ExitedUtc.HasValue ? ClientCondition.Exited : row.Condition;
            if (condition != ClientCondition.Exited && condition != ClientCondition.Unloaded) {
                if (!row.Measured) condition = ClientCondition.Unmeasured;
                // A global issue may concern another client or writing an exit report.
                // Keep specific observed delay/no-response evidence visible.
                else if (observerError != null && condition != ClientCondition.NoResponse && condition != ClientCondition.Delayed)
                    condition = ClientCondition.MeasurementUnavailable;
            }
            var result = new ClientStatusPresentation { Color = Theme.Muted };
            switch (condition) {
                case ClientCondition.Healthy:
                    result.Status = L("Responding", "Svarer");
                    result.Explanation = L("The character's game updates and monitoring replies are arriving normally.", "Karakterens spilopdateringer og overvågningssvar kommer som forventet.");
                    result.NextStep = L("No action needed.", "Du behøver ikke gøre noget.");
                    result.Color = Theme.Accent;
                    break;
                case ClientCondition.Delayed:
                    result.Status = L("Delayed", "Forsinket");
                    result.Explanation = L("Recent game updates or monitoring replies took longer than expected.", "Spilopdateringer eller overvågningssvar tog for nylig længere tid end forventet.");
                    result.NextStep = L("Check the game window. If the delay continues, save a diagnostic ZIP for help.", "Tjek spilvinduet. Hvis forsinkelsen fortsætter, kan du gemme en diagnose-ZIP til hjælp.");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.NoResponse:
                    result.Status = L("No response", "Intet svar");
                    result.Explanation = L("Game updates or monitoring replies have stopped arriving. This does not confirm a crash.", "Spilopdateringer eller overvågningssvar er holdt op med at komme. Det bekræfter ikke et crash.");
                    result.NextStep = L("Check the game window. Allow zoning to finish; if the game stays stuck, save a diagnostic ZIP.", "Tjek spilvinduet. Vent på at zoning er færdig; gem en diagnose-ZIP, hvis spillet bliver ved med at sidde fast.");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.MeasurementUnavailable:
                    result.Status = L("Measurement unavailable", "Måling utilgængelig");
                    result.Explanation = L("Nexus cannot get current monitoring data. The game may still be working normally.", "Nexus kan ikke hente aktuelle målinger. Spillet kan stadig fungere normalt.");
                    result.NextStep = L("Check the game window, then check that Nexus Health Monitor is enabled in AOSharp. Open setup help below.", "Tjek spilvinduet og derefter, at Nexus Health Monitor er aktiveret i AOSharp. Åbn opsætningshjælpen nedenfor.");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.Waiting:
                    result.Status = L("Checking…", "Kontrollerer…");
                    result.Explanation = L("Monitoring has started and is waiting for its first complete measurements.", "Overvågningen er startet og venter på de første komplette målinger.");
                    result.NextStep = L("Wait a few seconds. The status updates automatically.", "Vent et par sekunder. Status opdateres automatisk.");
                    break;
                case ClientCondition.Unloaded:
                    result.Status = L("Monitoring stopped", "Overvågning stoppet");
                    result.Explanation = L("The monitoring plugin was unloaded. This does not mean the game has closed.", "Overvågnings-pluginet blev fjernet fra klienten. Det betyder ikke, at spillet er lukket.");
                    result.NextStep = L("Enable Nexus Health Monitor in this character's AOSharp profile when you want measurements again.", "Aktivér Nexus Health Monitor i karakterens AOSharp-profil, når du vil have målinger igen.");
                    break;
                case ClientCondition.Exited:
                    result.Status = L("Game closed", "Spil lukket");
                    result.Explanation = L("This game process has ended. It may have been a normal logout or an unexpected exit.", "Denne spilproces er afsluttet. Det kan være normalt logout eller en uventet afslutning.");
                    result.NextStep = L("If this was unexpected, save a diagnostic ZIP before launching again.", "Hvis det var uventet, kan du gemme en diagnose-ZIP, før du starter igen.");
                    break;
                default:
                    result.Status = L("Not measured", "Ikke målt");
                    result.Explanation = L("Nexus has found the game window, but no monitoring plugin measurements are available.", "Nexus har fundet spilvinduet, men der er ingen målinger fra overvågnings-pluginet.");
                    result.NextStep = L("For optional monitoring, open setup help below and enable Nexus Health Monitor for this character in AOSharp.", "Åbn opsætningshjælpen nedenfor og aktivér Nexus Health Monitor for karakteren i AOSharp, hvis du ønsker overvågning.");
                    break;
            }
            if (!row.ExitedUtc.HasValue && row.Sample?.Zoning == true)
                result.Explanation += L(" The last measurement showed zoning in progress.", " Den seneste måling viste igangværende zoning.");
            result.ActionStatus = row.HasCastIssue ? L("Recent cast issue: falling", "Nyligt kasteproblem: fald") : "—";
            if (row.HasCastIssue) {
                result.Explanation += "\r\n" + (row.CastIssueConfirmed
                    ? L("The game reported that a nanoprogram could not be executed while falling.", "Spillet rapporterede, at et nanoprogram ikke kunne udføres under fald.")
                    : L("A nano cast was attempted while the game reported the character as falling. The cast result was not confirmed.", "Et nanoprogram blev forsøgt, mens spillet registrerede karakteren som faldende. Resultatet er ikke bekræftet."));
                result.Explanation += L(" Reports in this burst: ", " Rapporter i denne serie: ") + row.CastIssueCount + ".";
                string actionHelp = L("Let the character land, then retry the nano. If they look grounded, try moving to a clear spot first. Check the result in game. Alerts fade after 30 seconds without a new report; fading does not mean the cast succeeded.",
                    "Lad karakteren lande, og prøv nanoprogrammet igen. Hvis den står på jorden, så prøv først at flytte til et frit sted. Tjek resultatet i spillet. Advarsler udløber efter 30 sekunder uden en ny rapport; det betyder ikke, at nanoprogrammet lykkedes.");
                result.NextStep = condition == ClientCondition.Healthy ? actionHelp : result.NextStep + "\r\n" + actionHelp;
            }
            return result;
        }
    }

    internal sealed class ClientStatusDialog : EditorDialog
    {
        readonly Timer refresh = new Timer { Interval = 1000 };
        internal ClientStatusDialog(Func<string> details, Func<string> snapshot, Func<ClientObservation[]> observations)
            : this(details, snapshot, observations, () => null) { }

        internal ClientStatusDialog(Func<string> details, Func<string> snapshot, Func<ClientObservation[]> observations, Func<string> observerError)
            : base(L("Character status and help", "Karakterstatus og hjælp"), 650)
        {
            ClientSize = new Size(720, 600);
            MinimumSize = new Size(600, 520);
            FormBorderStyle = FormBorderStyle.Sizable;
            FitContentOnOpen = true;
            DialogLayout.RowStyles[1].SizeType = SizeType.AutoSize;
            ErrorLabel.AutoSize = true;
            ErrorLabel.MinimumSize = new Size(0, 24);
            ErrorLabel.Visible = false;
            ErrorLabel.TextChanged += (sender, args) => ErrorLabel.Visible = !string.IsNullOrWhiteSpace(ErrorLabel.Text);
            AddWrappedNote(L("See which characters are responding and what to do next. Select a character for help. These are local game measurements, not server ping.",
                "Se hvilke karakterer der svarer, og hvad du kan gøre. Vælg en karakter for hjælp. Det er lokale spilmålinger, ikke serverping."), "clientStatusGuide");
            var roster = new DataGridView { Name = "clientStatusRoster", Dock = DockStyle.Top, Height = 210,
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, AccessibleName = L("Character monitoring status", "Karakterernes overvågningsstatus") };
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "character", HeaderText = L("Character", "Karakter"), FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable });
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = L("Status", "Status"), FillWeight = 34, SortMode = DataGridViewColumnSortMode.NotSortable });
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "action", HeaderText = L("Recent action issue", "Nyligt handlingsproblem"), FillWeight = 36, SortMode = DataGridViewColumnSortMode.NotSortable });
            roster.RowTemplate.Height = 32;
            roster.Margin = new Padding(0, 0, 0, 8);
            Fields.Controls.Add(SlimScrolling.Wrap(roster));
            var explanation = AddWrappedNote("", "clientStatusExplanation");
            explanation.ForeColor = Theme.Foreground;
            var nextStep = AddWrappedNote("", "clientStatusNextStep");
            nextStep.ForeColor = Theme.Accent;
            ClientObservation[] displayed = new ClientObservation[0];
            bool updating = false;
            Action showSelection = () => {
                if (updating) return;
                string previousExplanation = explanation.Text, previousNextStep = nextStep.Text;
                int index = roster.CurrentCell?.RowIndex ?? -1;
                if (index < 0 || index >= displayed.Length) {
                    explanation.Text = L("No Nexus-managed game clients are currently listed.", "Der er ingen Nexus-styrede spilklienter på listen.");
                    nextStep.Text = L("Launch a character through Nexus. Enable monitoring in Settings → Accounts if you want responsiveness measurements.", "Start en karakter gennem Nexus. Aktivér overvågning i Indstillinger → Konti, hvis du ønsker målinger af svartider.");
                } else {
                    var state = ClientStatusPresentation.For(displayed[index], observerError());
                    explanation.Text = displayed[index].Name + " · " + state.Status + "\r\n" + state.Explanation;
                    nextStep.Text = L("Next step: ", "Næste skridt: ") + state.NextStep;
                }
                if (explanation.Text != previousExplanation || nextStep.Text != previousNextStep) QueueContentFit();
            };
            roster.CurrentCellChanged += (sender, args) => showSelection();

            var technicalToggle = Theme.Button(L("Show technical details", "Vis tekniske detaljer"));
            technicalToggle.Name = "clientTechnicalToggle";
            var helpActions = new FlowLayoutPanel { Name = "clientHelpActions", Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Margin = new Padding(0, 4, 0, 4) };
            technicalToggle.Margin = new Padding(0, 0, 8, 8);
            helpActions.Controls.Add(technicalToggle);
            Fields.Controls.Add(helpActions);
            var text = new NexusTextBox { Name = "clientMeasurements", ReadOnly = true, Multiline = true, Height = 210, Dock = DockStyle.Top,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface, ForeColor = Theme.Foreground };
            var technicalHost = SlimScrolling.Wrap(text);
            technicalHost.Visible = false;
            Fields.Controls.Add(technicalHost);
            technicalToggle.Click += (sender, args) => {
                FitContentOnOpen = false;
                technicalHost.Visible = !technicalHost.Visible;
                technicalToggle.Text = technicalHost.Visible ? L("Hide technical details", "Skjul tekniske detaljer") : L("Show technical details", "Vis tekniske detaljer");
                Fields.PerformLayout();
                QueueContentFit();
            };
            var setupToggle = Theme.Button(L("Monitoring setup help", "Hjælp til opsætning af overvågning"));
            setupToggle.Name = "clientSetupToggle";
            setupToggle.Margin = new Padding(0, 0, 8, 8);
            helpActions.Controls.Add(setupToggle);
            var setupHelp = AddWrappedNote(L("Monitoring is optional and requires AOSharp. In AOSharp, choose Add Plugin → NexusHealthPlugin.dll. Select each character, tick Nexus Health Monitor alongside your usual plugins, then Inject. If already injected, Eject first when it is safe to restart that character's plugins. Keep NexusTelemetry.Protocol.dll next to the plugin and keep Nexus running.",
                "Overvågning er valgfri og kræver AOSharp. Vælg Add Plugin → NexusHealthPlugin.dll i AOSharp. Vælg hver karakter, sæt flueben ved Nexus Health Monitor sammen med dine normale plugins, og tryk Inject. Hvis allerede indlæst: brug Eject først, når det er sikkert at genstarte karakterens plugins. Behold NexusTelemetry.Protocol.dll ved siden af pluginet og lad Nexus køre."), "measurementHelp");
            setupHelp.Visible = false;
            var folder = Theme.Button(L("Open measurement plugin folder", "Åbn mappen med måle-plugin"));
            folder.Name = "clientPluginFolder";
            folder.Margin = new Padding(0, 0, 0, 8);
            folder.Visible = false;
            folder.Click += (sender, args) => { try { Directory.CreateDirectory(ClientMonitor.HelperRoot); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ClientMonitor.HelperRoot) { UseShellExecute = true }); } catch (Exception e) { ErrorLabel.Text = e.Message; } };
            Fields.Controls.Add(folder);
            setupToggle.Click += (sender, args) => {
                FitContentOnOpen = false;
                setupHelp.Visible = !setupHelp.Visible;
                folder.Visible = setupHelp.Visible;
                setupToggle.Text = setupHelp.Visible ? L("Hide monitoring setup help", "Skjul hjælp til opsætning") : L("Monitoring setup help", "Hjælp til opsætning af overvågning");
                Fields.PerformLayout();
                QueueContentFit();
            };
            Action update = () => {
                string next = details(); if (text.Text != next) text.Text = next;
                var rows = observations() ?? new ClientObservation[0];
                string selectedKey = roster.CurrentRow?.Tag as string;
                updating = true;
                try {
                    while (roster.Rows.Count > rows.Length) roster.Rows.RemoveAt(roster.Rows.Count - 1);
                    while (roster.Rows.Count < rows.Length) roster.Rows.Add();
                    displayed = rows;
                    int selectedIndex = -1;
                    for (int i = 0; i < rows.Length; i++) {
                        var row = rows[i]; var state = ClientStatusPresentation.For(row, observerError());
                        string key = row.Pid.ToString() + "/" + row.StartTicks.ToString();
                        var target = roster.Rows[i]; target.Tag = key;
                        if (!Equals(target.Cells[0].Value, row.Name)) target.Cells[0].Value = row.Name;
                        if (!Equals(target.Cells[1].Value, state.Status)) target.Cells[1].Value = state.Status;
                        target.Cells[1].Style.ForeColor = state.Color;
                        if (!Equals(target.Cells[2].Value, state.ActionStatus)) target.Cells[2].Value = state.ActionStatus;
                        target.Cells[2].Style.ForeColor = row.HasCastIssue ? Theme.Amber : Theme.Muted;
                        if (key == selectedKey) selectedIndex = i;
                    }
                    if (rows.Length > 0 && (selectedIndex >= 0 || roster.CurrentCell == null))
                        roster.CurrentCell = roster.Rows[Math.Max(0, selectedIndex)].Cells[0];
                } finally { updating = false; }
                showSelection();
            };
            refresh.Tick += (sender, args) => update(); Shown += (sender, args) => { update(); refresh.Start(); };
            AddWrappedNote(L("Need help? Save a diagnostic ZIP with recent measurements and available exit reports. Names are replaced by aliases. Nothing is uploaded.",
                "Brug for hjælp? Gem en diagnose-ZIP med de seneste målinger og tilgængelige afslutningsrapporter. Navne erstattes af aliaser. Intet uploades."), "reportPrivacy");
            SaveButton.Text = L("Save diagnostic ZIP…", "Gem diagnose-ZIP…");
            SaveButton.Click += async (sender, args) => {
                using (var save = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = "AO-Nexus-client-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true }) {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    string report = snapshot(); var rows = observations();
                    SaveButton.Enabled = false;
                    try {
                        string temporary = save.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try {
                            await Task.Run(() => ClientReport.Export(temporary, report, ClientReport.WindowsEvidence(rows)));
                            if (File.Exists(save.FileName)) File.Replace(temporary, save.FileName, null); else File.Move(temporary, save.FileName);
                        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        ErrorLabel.Text = L("ZIP saved. Review it before sharing.", "ZIP gemt. Gennemse den før deling.");
                    } catch (Exception e) { ErrorLabel.Text = e.Message; }
                    finally { if (!IsDisposed) SaveButton.Enabled = true; }
                }
            };
            CancelAction.Text = L("Close", "Luk"); CancelAction.Click += (sender, args) => Close();
            AcceptButton = CancelAction;
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
}
