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
        readonly Dictionary<string,long> syncAlertsSeen=new Dictionary<string,long>();

        void InitializeClientStatus()
        {
            clientStatus = new LinkLabel { Name = "clientHealth", Visible = false, AutoSize = true, Margin = new Padding(0, 10, 16, 0), MaximumSize = new Size(380, 20), AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F), BackColor = Color.Transparent, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber,
                LinkBehavior = LinkBehavior.HoverUnderline, AccessibleName = AOLauncher.Localization.Texts.Get("Text_Character_status_3826BDC7320F") };
            clientStatus.LinkClicked += (sender, args) => ShowClientStatus();
            headerFlow.Controls.Add(clientStatus); // beside Settings in the header
            trayClientsItem = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Characters_status_and_help_E5F368A05B53"));
            trayClientsItem.Click += (sender, args) => BeginInvoke((Action)ShowClientStatus);
            trayMenu.Items.Insert(2, trayClientsItem);
            if (smoke || storageBlocked) return;
            clientMonitor = new ClientMonitor();
            clientMonitorTimer.Tick += async (sender, args) => {DevelopmentRecorder.Beat();await PollClients();};
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
                await Task.Run(() => {
                    var watch=System.Diagnostics.Stopwatch.StartNew();monitor.Poll(snapshot);
                    DevelopmentRecorder.Poll(monitor.Current,watch.Elapsed.TotalMilliseconds);
                });
                if (clientMonitorDisposed || closing || IsDisposed) return;
                clientObservations = monitor.Current;
                ReportSyncAlerts();
                clientReport = monitor.Capture(); clientObserverError = monitor.Error;
                foreach (var change in monitor.Changes) AppendLog(change);
                RefreshClientStatus(); RefreshHealthTray();
            } catch (Exception error) {
                DevelopmentRecorder.Write("monitor-error",new {Type=error.GetType().Name,Message=DiagnosticRedactor.Current.Redact(error.Message)});
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
        void ReportSyncAlerts()
        {
            var notices=new List<string>();
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            foreach(var row in clientObservations) {
                var movement=row.Pulse?.Movement;
                if(!MovementTracking.Fresh(row,now) || movement.GuardEventQpc<=0 || now<movement.GuardEventQpc
                    || now-movement.GuardEventQpc>10*System.Diagnostics.Stopwatch.Frequency)continue;
                string key=row.Pid+"/"+row.StartTicks;
                if(syncAlertsSeen.TryGetValue(key,out var seen) && seen>=movement.GuardEventQpc)continue;
                syncAlertsSeen[key]=movement.GuardEventQpc;
                DevelopmentRecorder.Write("sync-guard-event",new {row.Pid,row.StartTicks,movement.GuardEvent,movement.GuardEventQpc,movement.GuardDistance,movement.GuardLeader});
                if(movement.GuardEvent!="Warning" && movement.GuardEvent!="Stopped")continue;
                string message=string.Format(AOLauncher.Localization.Texts.Get("Movement.Alert"+movement.GuardEvent),row.Name,movement.GuardDistance);
                AppendLog(message);
                DevelopmentRecorder.Write("sync-guard-alert",new {row.Pid,row.StartTicks,movement.GuardEvent,movement.GuardEventQpc,movement.GuardDistance,movement.GuardLeader});
                notices.Add(message);
            }
            if(notices.Count==0)return;
            System.Media.SystemSounds.Exclamation.Play();
            if(trayIcon!=null) { trayIcon.Visible=true;trayIcon.ShowBalloonTip(10000,"AO Nexus",string.Join("\n",notices),ToolTipIcon.Warning); }
        }
        bool ClientWarning => MeasuredClients.Any(c => c.Condition == ClientCondition.Delayed || c.Condition == ClientCondition.NoResponse || c.Condition == ClientCondition.MeasurementUnavailable || c.HasCastIssue) || clientObserverError != null && MeasuredClients.Any();
        bool NewClientExit => clientObservations.Any(c => c.ExitedUtc.HasValue && c.ExitedUtc.Value > exitsSeenUtc);
        string ClientSummary()
        {
            var rows = MeasuredClients.ToArray();
            if (rows.Length == 0) {
                if (NewClientExit) return AOLauncher.Localization.Texts.Get("Text_Characters_game_closed_view_details_88B1F9FBFC7C");
                if (settings.MonitorClientHealth && clientObservations.Any(c => !c.ExitedUtc.HasValue)) {
                    var live = clientObservations.Where(c => !c.ExitedUtc.HasValue).ToArray();
                    int stopped = live.Count(c => c.Condition == ClientCondition.Unloaded);
                    if (stopped == live.Length)
                        return AOLauncher.Localization.Texts.Get("Text_Characters_monitoring_stopped_click_for_help_5C3C489F106B");
                    if (stopped > 0)
                        return AOLauncher.Localization.Texts.Get("Text_Characters_F0A1A9369C4C") + stopped + AOLauncher.Localization.Texts.Get("Text_monitoring_stopped_58996E24FEC2")
                            + " · " + (live.Length - stopped) + AOLauncher.Localization.Texts.Get("Text_not_measured_D5D3B63D44E1");
                    return AOLauncher.Localization.Texts.Get("Text_Characters_monitoring_not_set_up_click_3332C2DB0135");
                }
                return null;
            }
            var bad = rows.FirstOrDefault(c => c.Condition == ClientCondition.NoResponse) ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.Delayed)
                ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.MeasurementUnavailable);
            if (bad != null) return bad.Name + " · " + ClientStatusPresentation.For(bad, clientObserverError).Status;
            if (clientObserverError != null) return AOLauncher.Localization.Texts.Get("Text_Characters_measurement_unavailable_A5EB9E93CC5D");
            var actionIssue = rows.FirstOrDefault(c => c.HasCastIssue);
            if (actionIssue != null) return actionIssue.Name + AOLauncher.Localization.Texts.Get("Text_recent_cast_issue_falling_13D1DE52141C");
            int healthy = rows.Count(c => c.Condition == ClientCondition.Healthy);
            int unmeasured = clientObservations.Count(c => !c.ExitedUtc.HasValue && !c.Measured);
            return AOLauncher.Localization.Texts.Get("Text_Characters_F0A1A9369C4C") + healthy + "/" + rows.Length + AOLauncher.Localization.Texts.Get("Text_responding_327A70FCA581") + (unmeasured == 0 ? "" : " · " + unmeasured + AOLauncher.Localization.Texts.Get("Text_not_measured_D5D3B63D44E1"));
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
            clientStatus.AccessibleName = AOLauncher.Localization.Texts.Get("Text_Character_status_3826BDC7320F");
            if (trayClientsItem != null) trayClientsItem.Text = AOLauncher.Localization.Texts.Get("Text_Characters_status_and_help_E5F368A05B53");
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
                    result.AppendFormat(AOLauncher.Localization.Texts.Get("Text_IPC_0_ms_1_timeouts_2_DEF297CC16D2"), s.ReplyMs?.ToString("F1") ?? "?", s.Timeouts?.ToString() ?? "?", s.Late?.ToString() ?? "?");
                    result.AppendFormat("\r\n  {0}: {1} ms · max/10s: {2} ms · max: {3} ms", AOLauncher.Localization.Texts.Get("Text_Update_gap_at_snapshot_38F20EF748C1"),
                        s.ObservedUpdateGapMs?.ToString("F0") ?? "?", s.RecentMaxGapMs?.ToString("F1") ?? "?", s.MaxGapMs?.ToString("F1") ?? "?");
                    result.AppendFormat(AOLauncher.Localization.Texts.Get("Text_Snapshot_age_0_ms_helper_age_F1A2AEEAEABA"),
                        s.PulseAgeMs?.ToString("F0") ?? "?", s.HelperAgeMs?.ToString("F0") ?? "?", s.ReplyAgeMs?.ToString("F0") ?? "?");
                    if (s.Reason != null) result.Append("\r\n  ").Append(s.Reason);
                    if (s.Minimized) result.Append(AOLauncher.Localization.Texts.Get("Text_minimized_29BCD54A1780"));
                    if (s.Zoning == true) result.Append(AOLauncher.Localization.Texts.Get("Text_zoning_last_observed_AE61CE5C663B"));
                }
                result.AppendLine();
            }
            if (result.Length == 0) result.AppendLine(AOLauncher.Localization.Texts.Get("Text_No_Nexus_managed_clients_detected_7EACFFAFAB29"));
            if (clientObserverError != null) result.AppendLine(AOLauncher.Localization.Texts.Get("Text_Measurement_830369828612") + clientObserverError);
            return result.ToString();
        }
        void ShowClientStatus()
        {
            ShowLauncher(); exitsSeenUtc = DateTime.UtcNow; RefreshClientStatus(); RefreshHealthTray();
            using (var dialog = new ClientStatusDialog(() => ClientDetails(), () => clientReport, () => clientObservations.ToArray(), () => clientObserverError) { ActivityLog = () => activityLog.ToString(), AppState = AppStateSnapshot }) dialog.ShowDialog(this);
        }
        void DisposeClientMonitor()
        {
            if(!smoke)DevelopmentRecorder.Stop();
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
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Responding_35E6019771E7");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_The_character_s_game_updates_and_E58D4B7718AE");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_No_action_needed_72E437159AC3");
                    result.Color = Theme.Accent;
                    break;
                case ClientCondition.Delayed:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Delayed_17C12ABE1A14");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_Recent_game_updates_or_monitoring_replies_E0C22D51B690");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Check_the_game_window_If_the_329DF7FA7AC5");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.NoResponse:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_No_response_1C26BD324EDD");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_Game_updates_or_monitoring_replies_have_53EB7F285648");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Check_the_game_window_Allow_zoning_F8E86AA85EAD");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.MeasurementUnavailable:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Measurement_unavailable_20443020E9B2");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_Nexus_cannot_get_current_monitoring_data_978FED481A2F");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Check_the_game_window_then_check_0D3492F76E1D");
                    result.Color = Theme.Warning;
                    break;
                case ClientCondition.Waiting:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Checking_B5725BB70893");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_Monitoring_has_started_and_is_waiting_C3AA70434161");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Wait_a_few_seconds_The_status_15B2AC68DE2F");
                    break;
                case ClientCondition.Unloaded:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Monitoring_stopped_3616F7F5EF23");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_The_monitoring_plugin_was_unloaded_This_D823F8EC782D");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Enable_Nexus_Health_Monitor_in_this_B266DA419FDD");
                    break;
                case ClientCondition.Exited:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Game_closed_56CECD673BDA");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_This_game_process_has_ended_It_668111C55FF5");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_If_this_was_unexpected_save_a_5F5852AD2C8F");
                    break;
                default:
                    result.Status = AOLauncher.Localization.Texts.Get("Text_Not_measured_61E24E1E3CEB");
                    result.Explanation = AOLauncher.Localization.Texts.Get("Text_Nexus_has_found_the_game_window_466C91B6A9A2");
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_For_optional_monitoring_open_setup_help_20662E43FBAA");
                    break;
            }
            if (!row.ExitedUtc.HasValue && row.Sample?.Zoning == true)
                result.Explanation += AOLauncher.Localization.Texts.Get("Text_The_last_measurement_showed_zoning_in_D059DC5228B7");
            if (condition == ClientCondition.Unmeasured && row.SnapshotStatus != null) {
                result.Explanation = ClientMonitor.SnapshotStatusText(row.SnapshotStatus);
                if (row.SnapshotStatus == "MonitoringDisabled")
                    result.NextStep = AOLauncher.Localization.Texts.Get("Text_Enable_responsiveness_monitoring_in_Settings_Accounts_5A494729D72A");
            }
            result.ActionStatus = row.HasCastIssue ? AOLauncher.Localization.Texts.Get("Text_Recent_cast_issue_falling_C3EE52C0C006") : "—";
            if (row.HasCastIssue) {
                result.Explanation += "\r\n" + (row.CastIssueConfirmed
                    ? AOLauncher.Localization.Texts.Get("Text_The_game_reported_that_a_nanoprogram_BDA2D1110733")
                    : AOLauncher.Localization.Texts.Get("Text_A_nano_cast_was_attempted_while_821B0FC8F965"));
                result.Explanation += AOLauncher.Localization.Texts.Get("Text_Reports_in_this_burst_CE1BEA2A540D") + row.CastIssueCount + ".";
                string actionHelp = AOLauncher.Localization.Texts.Get("Text_Let_the_character_land_then_retry_949F8B3BB5E8");
                result.NextStep = condition == ClientCondition.Healthy ? actionHelp : result.NextStep + "\r\n" + actionHelp;
            }
            return result;
        }
    }

    internal sealed class ClientStatusDialog : EditorDialog
    {
        readonly Timer refresh = new Timer { Interval = 1000 };
        // This session's activity log, added (anonymised) to the diagnostic ZIP.
        internal Func<string> ActivityLog { private get; set; }
        // How Nexus is set up right now: { app-state.txt, settings-snapshot.json }.
        internal Func<string[]> AppState { private get; set; }
        internal ClientStatusDialog(Func<string> details, Func<string> snapshot, Func<ClientObservation[]> observations)
            : this(details, snapshot, observations, () => null) { }

        internal ClientStatusDialog(Func<string> details, Func<string> snapshot, Func<ClientObservation[]> observations, Func<string> observerError)
            : base(AOLauncher.Localization.Texts.Get("Text_Character_status_and_help_C134ED828E98"), 650)
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
            AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_See_which_characters_are_responding_and_6442A358D311"), "clientStatusGuide");
            var roster = new DataGridView { Name = "clientStatusRoster", Dock = DockStyle.Top, Height = 210,
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, AccessibleName = AOLauncher.Localization.Texts.Get("Text_Character_monitoring_status_10981055AB18") };
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "character", HeaderText = AOLauncher.Localization.Texts.Get("Text_Character_C8A1DC62F40D"), FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable });
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "status", HeaderText = AOLauncher.Localization.Texts.Get("Text_Status_7C6F7141D85E"), FillWeight = 34, SortMode = DataGridViewColumnSortMode.NotSortable });
            roster.Columns.Add(new DataGridViewTextBoxColumn { Name = "action", HeaderText = AOLauncher.Localization.Texts.Get("Text_Recent_action_issue_C05852337CE9"), FillWeight = 36, SortMode = DataGridViewColumnSortMode.NotSortable });
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
                    explanation.Text = AOLauncher.Localization.Texts.Get("Text_No_Nexus_managed_game_clients_are_6ADDF6EB4573");
                    nextStep.Text = AOLauncher.Localization.Texts.Get("Text_Launch_a_character_through_Nexus_Enable_BDE638A9AAE5");
                } else {
                    var state = ClientStatusPresentation.For(displayed[index], observerError());
                    explanation.Text = displayed[index].Name + " · " + state.Status + "\r\n" + state.Explanation;
                    nextStep.Text = AOLauncher.Localization.Texts.Get("Text_Next_step_445F0D9740CC") + state.NextStep;
                }
                if (explanation.Text != previousExplanation || nextStep.Text != previousNextStep) QueueContentFit();
            };
            roster.CurrentCellChanged += (sender, args) => showSelection();

            var technicalToggle = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Show_technical_details_3B6D42F1CC8C"));
            technicalToggle.Name = "clientTechnicalToggle";
            var helpActions = new FlowLayoutPanel { Name = "clientHelpActions", Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Margin = new Padding(0, 4, 0, 4) };
            technicalToggle.Margin = new Padding(0, 0, 8, 8);
            helpActions.Controls.Add(technicalToggle);
            var recording=new NexusCheckBox {Name="developmentRecording",Text=AOLauncher.Localization.Texts.Get("Text_Record_diagnostics_B08CD96A6D9C"),AutoSize=false,Size=new Size(180,28),Checked=DevelopmentRecorder.Enabled,Margin=new Padding(8,0,0,8)};
            recording.CheckedChanged+=(sender,args)=> {
                try {DevelopmentRecorder.SetEnabled(recording.Checked);}
                catch(Exception error) {ErrorLabel.Text=error.Message;}
            };
            helpActions.Controls.Add(recording);
            Fields.Controls.Add(helpActions);
            var recordingStatus=AddWrappedNote("", "developmentRecordingStatus");
            var text = new NexusTextBox { Name = "clientMeasurements", ReadOnly = true, Multiline = true, Height = 210, Dock = DockStyle.Top,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface, ForeColor = Theme.Foreground };
            var technicalHost = SlimScrolling.Wrap(text);
            technicalHost.Visible = false;
            Fields.Controls.Add(technicalHost);
            technicalToggle.Click += (sender, args) => {
                FitContentOnOpen = false;
                technicalHost.Visible = !technicalHost.Visible;
                technicalToggle.Text = technicalHost.Visible ? AOLauncher.Localization.Texts.Get("Text_Hide_technical_details_989661EBFA55") : AOLauncher.Localization.Texts.Get("Text_Show_technical_details_3B6D42F1CC8C");
                Fields.PerformLayout();
                QueueContentFit();
            };
            var setupToggle = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Monitoring_setup_help_F1EAE046BDF7"));
            setupToggle.Name = "clientSetupToggle";
            setupToggle.Margin = new Padding(0, 0, 8, 8);
            helpActions.Controls.Add(setupToggle);
            var setupHelp = AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_Monitoring_is_optional_and_requires_AO_14BCF8398723"), "measurementHelp");
            setupHelp.Visible = false;
            var folder = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Open_measurement_plugin_folder_444C30164A07"));
            folder.Name = "clientPluginFolder";
            folder.Margin = new Padding(0, 0, 0, 8);
            folder.Visible = false;
            folder.Click += (sender, args) => { try { Directory.CreateDirectory(ClientMonitor.HelperRoot); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ClientMonitor.HelperRoot) { UseShellExecute = true }); } catch (Exception e) { ErrorLabel.Text = e.Message; } };
            Fields.Controls.Add(folder);
            setupToggle.Click += (sender, args) => {
                FitContentOnOpen = false;
                setupHelp.Visible = !setupHelp.Visible;
                folder.Visible = setupHelp.Visible;
                setupToggle.Text = setupHelp.Visible ? AOLauncher.Localization.Texts.Get("Text_Hide_monitoring_setup_help_40638619D4D9") : AOLauncher.Localization.Texts.Get("Text_Monitoring_setup_help_F1EAE046BDF7");
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
            Action updateRecording=()=> {
                recordingStatus.Text=DevelopmentRecorder.Enabled
                    ? (DevelopmentRecorder.Error==null?AOLauncher.Localization.Texts.Get("Text_Recording_diagnostics_locally_E651077DB60A"):AOLauncher.Localization.Texts.Get("Text_Recording_failed_3BFE84FAFC96")+DevelopmentRecorder.Error)
                    : AOLauncher.Localization.Texts.Get("Text_Diagnostic_recording_is_off_F7EAA9741DE4");
            };
            refresh.Tick += (sender, args) => {update();updateRecording();}; Shown += (sender, args) => { update();updateRecording();refresh.Start(); };
            AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_Need_help_Save_a_diagnostic_ZIP_643B81AD8C9D"), "reportPrivacy");
            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Save_diagnostic_ZIP_61E8BC323A2D");
            SaveButton.Click += async (sender, args) => {
                using (var save = new SaveFileDialog { Filter = "ZIP (*.zip)|*.zip", FileName = "AO-Nexus-client-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true }) {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    string report = snapshot();
                    string log = ActivityLog?.Invoke();
                    string[] state = null;
                    try { state = AppState?.Invoke(); } catch (Exception e) { state = new[] { "The app state could not be collected: " + e.Message, null }; }
                    SaveButton.Enabled = false;
                    try {
                        string temporary = save.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try {
                            await Task.Run(() => ClientReport.Export(temporary, report, null, null, log, state?[0], state?.Length > 1 ? state[1] : null));
                            if (File.Exists(save.FileName)) File.Replace(temporary, save.FileName, null); else File.Move(temporary, save.FileName);
                        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_ZIP_saved_Review_it_before_sharing_67656814773C");
                    } catch (Exception e) { ErrorLabel.Text = e.Message; }
                    finally { if (!IsDisposed) SaveButton.Enabled = true; }
                }
            };
            CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175"); CancelAction.Click += (sender, args) => Close();
            AcceptButton = CancelAction;
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
}
