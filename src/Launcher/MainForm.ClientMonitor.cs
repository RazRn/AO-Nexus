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
                LinkBehavior = LinkBehavior.HoverUnderline, AccessibleName = L("AOSharp client status", "AOSharp-klientstatus") };
            clientStatus.LinkClicked += (sender, args) => ShowClientStatus();
            header.Controls.Add(clientStatus);
            trayClientsItem = new ToolStripMenuItem(L("Clients · measurements and exit reports", "Klienter · målinger og exit-rapporter"));
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
        bool ClientWarning => MeasuredClients.Any(c => c.Condition == ClientCondition.Delayed || c.Condition == ClientCondition.NoResponse || c.Condition == ClientCondition.MeasurementUnavailable) || clientObserverError != null && MeasuredClients.Any();
        bool NewClientExit => clientObservations.Any(c => c.ExitedUtc.HasValue && c.ExitedUtc.Value > exitsSeenUtc);
        string ClientSummary()
        {
            var rows = MeasuredClients.ToArray();
            if (rows.Length == 0) {
                if (NewClientExit) return L("AO · client exited · report", "AO · klient afsluttet · rapport");
                if (settings.MonitorClientHealth && clientObservations.Any(c => !c.ExitedUtc.HasValue))
                    return L("AOSharp · not measured · click to set up", "AOSharp · ikke målt · klik for opsætning");
                return null;
            }
            var bad = rows.FirstOrDefault(c => c.Condition == ClientCondition.NoResponse) ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.Delayed)
                ?? rows.FirstOrDefault(c => c.Condition == ClientCondition.MeasurementUnavailable);
            if (bad != null) return "AOSharp · " + bad.Name + ": " + ClientMonitor.ConditionText(bad.Condition);
            if (clientObserverError != null) return L("AOSharp · measurement unavailable", "AOSharp · måling utilgængelig");
            int healthy = rows.Count(c => c.Condition == ClientCondition.Healthy);
            int unmeasured = clientObservations.Count(c => !c.ExitedUtc.HasValue && !c.Measured);
            return "AOSharp · " + healthy + "/" + rows.Length + L(" responding", " svarer") + (unmeasured == 0 ? "" : " · " + unmeasured + L(" unmeasured", " ikke målt"));
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
            string summary = ClientSummary();
            clientStatus.Visible = summary != null;
            if (summary != null) clientStatus.Text = summary;
            clientStatus.LinkColor = ClientWarning ? Theme.Warning : MeasuredClients.Any() ? Theme.Accent : Theme.Muted;
            string details = ClientDetails();
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
            using (var dialog = new ClientStatusDialog(() => ClientDetails(), () => clientReport, () => clientObservations.ToArray())) dialog.ShowDialog(this);
        }
        void DisposeClientMonitor()
        {
            clientMonitorDisposed = true; clientMonitorTimer.Stop(); clientMonitorTimer.Dispose(); clientTooltip.Dispose();
            if (!clientPollBusy) { clientMonitor?.Dispose(); clientMonitor = null; }
        }
    }
    internal sealed class ClientStatusDialog : EditorDialog
    {
        readonly Timer refresh = new Timer { Interval = 1000 };
        internal ClientStatusDialog(Func<string> details, Func<string> snapshot, Func<ClientObservation[]> observations)
            : base(L("Client responsiveness and exit reports", "Klientrespons og exit-rapporter"), 600)
        {
            AddWrappedNote(L("In AOSharp: Add Plugin → NexusHealthPlugin.dll. Select each character, tick Nexus Health Monitor alongside your usual plugins, then Inject. If already injected, Eject first when it is safe to restart that character's plugins. Keep Nexus running. No response is not proof of a crash; zoning or a blocked measurement path can also cause it.",
                "I AOSharp: Add Plugin → NexusHealthPlugin.dll. Vælg hver char, sæt flueben ved Nexus Health Monitor sammen med dine normale plugins, og tryk Inject. Hvis allerede indlæst: brug Eject først, når det er sikkert at genstarte charens plugins. Lad Nexus køre. Intet svar beviser ikke et crash; zoning eller en blokeret måling kan også være årsagen."), "measurementHelp");
            var folder = Theme.Button(L("Open measurement plugin folder", "Åbn mappen med måle-plugin"));
            folder.Click += (sender, args) => { try { Directory.CreateDirectory(ClientMonitor.HelperRoot); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ClientMonitor.HelperRoot) { UseShellExecute = true }); } catch (Exception e) { ErrorLabel.Text = e.Message; } };
            Fields.Controls.Add(folder);
            var text = new NexusTextBox { Name = "clientMeasurements", ReadOnly = true, Multiline = true, Height = 280, Dock = DockStyle.Top,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.Surface, ForeColor = Theme.Foreground };
            Fields.Controls.Add(SlimScrolling.Wrap(text));
            Action update = () => { string next = details(); if (text.Text != next) text.Text = next; };
            refresh.Tick += (sender, args) => update(); Shown += (sender, args) => { update(); refresh.Start(); };
            AddWrappedNote(L("Save ZIP: recent measurements, up to 20 saved exits, and matching Windows error events. Names are replaced by aliases. No passwords, chat or memory dumps. Nothing is uploaded.",
                "Gem ZIP: seneste målinger, op til 20 gemte exits og matchende Windows-fejl. Navne erstattes af aliaser. Ingen passwords, chat eller hukommelsesdumps. Intet uploades."), "reportPrivacy");
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
