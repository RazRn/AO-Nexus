using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class MainForm
    {
        private readonly HealthAlerts healthAlerts = new HealthAlerts();
        private ToolStripMenuItem trayHealthItem;
        private string lastHealthChange;
        readonly List<HealthSnapshot> panelHealthHistory=new List<HealthSnapshot>();

        private void ConfigureHealthAlerts()
        {
            var ids = new HashSet<string>(settings.HealthServerIds ?? new List<string> { "rebirth", "prk", "live", "live-2019" });
            healthAlerts.Retain(settings.LoginServers.Where(server => ids.Contains(server.Id))
                .Select(server => ServerHealth.EndpointKey(server) + "|" + HealthEndpoints.Resolve(server, settings.HealthCheckEndpoints).Key));
            RefreshHealthTray();
        }
        private void HandleHealthChecks(List<HealthSnapshot> snapshots)
        {
            if (closing || IsDisposed) return;
            panelHealthHistory.AddRange(snapshots);
            panelHealthHistory.RemoveAll(s=>s.Results.All(r=>r.CheckedUtc<DateTime.UtcNow.AddMinutes(-5)));
            var transitions = snapshots.SelectMany(snapshot => healthAlerts.Apply(snapshot, DateTime.UtcNow)).ToList();
            foreach (var change in transitions) AppendLog(AOLauncher.Localization.Texts.Get("Text_Server_health_54D2F2EE2365") + change.Text);
            if (transitions.Count > 0) lastHealthChange = DateTime.Now.ToString("HH:mm") + " · " + string.Join("; ", transitions.Select(change => change.Text));
            RefreshHealthTray();
            if (!settings.HealthTrayNotifications || transitions.Count == 0 || smoke || trayIcon == null) return;
            string text = string.Join("\r\n", transitions.Take(3).Select(change => change.Text));
            if (transitions.Count > 3) text += "\r\n+ " + (transitions.Count - 3) + AOLauncher.Localization.Texts.Get("Text_changes_Open_tray_status_for_details_A8B72FC8D5C1");
            if (text.Length > 700) text = text.Substring(0, 697) + "…";
            // Windows controls display duration and notification suppression. Persistent evidence
            // stays in the tray menu and activity log, not in repeated popup reminders.
            trayIcon.ShowBalloonTip(10000, "AO Nexus · " + AOLauncher.Localization.Texts.Get("Text_Server_health_D800F82EBE9D"), text,
                transitions.Any(change => !change.Recovered) ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private void RefreshHealthTray()
        {
            if (trayIcon == null || trayHealthItem == null || closing) return;
            bool enabled = settings.BackgroundHealthChecks || settings.HealthTrayNotifications;
            trayIcon.Visible = hiddenInTray || settings.HealthTrayNotifications || MeasuredClients.Any() || NewClientExit
                || settings.MonitorClientHealth && clientObservations.Any(c => !c.ExitedUtc.HasValue);
            trayIcon.Icon = enabled && healthAlerts.IssueCount > 0 || ClientWarning ? SystemIcons.Warning : Icon;
            string summary = !enabled ? AOLauncher.Localization.Texts.Get("Text_Background_checks_off_BD7F9D634FF6")
                : healthAlerts.IssueCount > 0 ? healthAlerts.IssueCount + AOLauncher.Localization.Texts.Get("Text_unresolved_issue_s_012274D7ECD9")
                : !healthAlerts.Observations.Any() ? AOLauncher.Localization.Texts.Get("Text_Waiting_for_checks_5F8141C82D79")
                : healthAlerts.Observations.Any(item => item.State == HealthState.Unknown || item.Failures > 0 || DateTime.UtcNow - item.CheckedUtc > TimeSpan.FromSeconds(150))
                    ? AOLauncher.Localization.Texts.Get("Text_Some_checks_unknown_or_pending_E52C35250E75") : AOLauncher.Localization.Texts.Get("Text_Checked_services_reachable_24DD06233A03");
            string clientSummary = ClientSummary();
            string tip = "AO Nexus · " + (clientSummary ?? summary);
            trayIcon.Text = LimitTrayText(tip);
            trayHealthItem.Text = AOLauncher.Localization.Texts.Get("Text_Server_health_3154C2237D21") + summary;
        }
        private void PopulateHealthMenu()
        {
            RefreshHealthTray();
            StyleHealthMenu(trayHealthItem);
            foreach (ToolStripItem item in trayHealthItem.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
            trayHealthItem.DropDownItems.Clear();
            if (!string.IsNullOrEmpty(lastHealthChange)) trayHealthItem.DropDownItems.Add(new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Last_change_5B48DA3624FE") + lastHealthChange) { Enabled = false });
            foreach (var group in healthAlerts.Observations.GroupBy(item => item.ServerName).OrderBy(group => group.Key))
            {
                var server = new ToolStripMenuItem(group.Key);
                StyleHealthMenu(server);
                foreach (var observation in group)
                    server.DropDownItems.Add(new ToolStripMenuItem(HealthObservation.ServiceName(observation.Service) + ": " + observation.Status(DateTime.UtcNow)) { Enabled = false });
                trayHealthItem.DropDownItems.Add(server);
            }
            var open = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Open_server_health_89DE90432403"));
            open.Click += (sender, e) => BeginInvoke((Action)ShowHealthFromTray);
            trayHealthItem.DropDownItems.Add(open);
        }
        private static void StyleHealthMenu(ToolStripMenuItem parent)
        {
            parent.ForeColor = Theme.Foreground;
            parent.DropDown.BackColor = Theme.SurfaceRaised;
            parent.DropDown.ForeColor = Theme.Foreground;
            parent.DropDown.Renderer = new NewsPanel.SourceMenuRenderer();
            parent.DropDown.ShowItemToolTips = false;
        }
        private void ShowHealthFromTray()
        {
            ShowLauncher();
            if (serverSidebar.Tabs != null && settings.ShowServerHealth) serverSidebar.Tabs.SelectedIndex = 1; // both panels are visible when stacked
            if (currentSection != "characters") _ = ShowSection("characters");
        }
    }
}
