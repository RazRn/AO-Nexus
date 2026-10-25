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
            var transitions = snapshots.SelectMany(snapshot => healthAlerts.Apply(snapshot, DateTime.UtcNow)).ToList();
            foreach (var change in transitions) AppendLog(L("Server health: ", "Serverstatus: ") + change.Text);
            if (transitions.Count > 0) lastHealthChange = DateTime.Now.ToString("HH:mm") + " · " + string.Join("; ", transitions.Select(change => change.Text));
            RefreshHealthTray();
            if (!settings.HealthTrayNotifications || transitions.Count == 0 || smoke || trayIcon == null) return;
            string text = string.Join("\r\n", transitions.Take(3).Select(change => change.Text));
            if (transitions.Count > 3) text += "\r\n+ " + (transitions.Count - 3) + L(" changes. Open tray status for details.", " ændringer. Åbn bakkestatus for detaljer.");
            if (text.Length > 700) text = text.Substring(0, 697) + "…";
            // Windows controls display duration and notification suppression. Persistent evidence
            // stays in the tray menu and activity log, not in repeated popup reminders.
            trayIcon.ShowBalloonTip(10000, "AO Nexus · " + L("Server health", "Serverstatus"), text,
                transitions.Any(change => !change.Recovered) ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        private void RefreshHealthTray()
        {
            if (trayIcon == null || trayHealthItem == null || closing) return;
            bool enabled = settings.BackgroundHealthChecks || settings.HealthTrayNotifications;
            trayIcon.Visible = hiddenInTray || settings.HealthTrayNotifications;
            trayIcon.Icon = enabled && healthAlerts.IssueCount > 0 ? SystemIcons.Warning : Icon;
            string summary = !enabled ? L("Background checks off", "Baggrundstjek slået fra")
                : healthAlerts.IssueCount > 0 ? healthAlerts.IssueCount + L(" unresolved issue(s)", " uafklarede problemer")
                : !healthAlerts.Observations.Any() ? L("Waiting for checks", "Venter på tjek")
                : healthAlerts.Observations.Any(item => item.State == HealthState.Unknown || item.Failures > 0 || DateTime.UtcNow - item.CheckedUtc > TimeSpan.FromSeconds(150))
                    ? L("Some checks unknown or pending", "Nogle tjek ukendte eller afventer") : L("Checked services reachable", "Tjekkede tjenester tilgængelige");
            string tip = "AO Nexus · " + summary;
            trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
            trayHealthItem.Text = L("Server health · ", "Serverstatus · ") + summary;
        }
        private void PopulateHealthMenu()
        {
            RefreshHealthTray();
            StyleHealthMenu(trayHealthItem);
            foreach (ToolStripItem item in trayHealthItem.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
            trayHealthItem.DropDownItems.Clear();
            if (!string.IsNullOrEmpty(lastHealthChange)) trayHealthItem.DropDownItems.Add(new ToolStripMenuItem(L("Last change: ", "Seneste ændring: ") + lastHealthChange) { Enabled = false });
            foreach (var group in healthAlerts.Observations.GroupBy(item => item.ServerName).OrderBy(group => group.Key))
            {
                var server = new ToolStripMenuItem(group.Key);
                StyleHealthMenu(server);
                foreach (var observation in group)
                    server.DropDownItems.Add(new ToolStripMenuItem(HealthObservation.ServiceName(observation.Service) + ": " + observation.Status(DateTime.UtcNow)) { Enabled = false });
                trayHealthItem.DropDownItems.Add(server);
            }
            var open = new ToolStripMenuItem(L("Open server health", "Åbn serverstatus"));
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
            if (serverSidebar.Tabs != null && settings.ShowServerHealth) serverSidebar.Tabs.SelectedIndex = 1;
        }
    }
}
