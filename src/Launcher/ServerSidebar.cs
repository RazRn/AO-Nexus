using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class SidebarOptions : UserControl
    {
        private readonly CheckBox news, health, background, notifications;
        private readonly Dictionary<string, CheckBox> sources = new Dictionary<string, CheckBox>();
        private readonly FlowLayoutPanel servers;
        internal bool ShowNews => news.Checked;
        internal bool ShowHealth => health.Checked;
        internal bool BackgroundChecks => background.Checked;
        internal bool TrayNotifications => notifications.Checked;
        internal List<string> NewsIds => sources.Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToList();
        internal List<string> HealthIds => servers.Controls.OfType<CheckBox>().Where(box => box.Checked).Select(box => (string)box.Tag).ToList();
        internal SidebarOptions(LauncherSettings settings)
        {
            Dock = DockStyle.Fill; BackColor = Theme.Background; Name = "sidebarOptions";
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = Theme.Background };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            news = Check(L("Show Server news", "Vis servernyheder"), settings.ShowServerNews, "showServerNews");
            fields.Controls.Add(news);
            var feedChoices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(18, 0, 0, 14) };
            foreach (var source in ServerNews.Sources)
            {
                var box = Check(source.Name, ServerNews.NormalizeSourceIds(settings.NewsSourceIds).Contains(source.Id), "newsSource-" + source.Id);
                sources.Add(source.Id, box); feedChoices.Controls.Add(box);
            }
            fields.Controls.Add(feedChoices);
            health = Check(L("Show Server health", "Vis serverstatus"), settings.ShowServerHealth, "showServerHealth");
            fields.Controls.Add(health);
            background = Check(L("Check selected servers in the background", "Tjek valgte servere i baggrunden"), settings.BackgroundHealthChecks || settings.HealthTrayNotifications, "backgroundHealthChecks");
            notifications = Check(L("Tray notifications for outages and recovery", "Bakkemeddelelser ved udfald og genoprettelse"), settings.HealthTrayNotifications, "healthTrayNotifications");
            fields.Controls.Add(background); fields.Controls.Add(notifications);
            servers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(18, 0, 0, 14) };
            fields.Controls.Add(servers);
            RebuildServers(settings.LoginServers, settings.HealthServerIds ?? new List<string> { "rebirth", "prk", "live", "live-2019" });
            var note = new Label { Text = L("Background checks run every 60 seconds while Nexus is open, including in the tray. Notifications enable background checks and report issues after two failed checks, then recovery. Windows controls popup duration; the tray keeps the outage status. Without background checks, only the visible health view is checked.", "Baggrundstjek kører hvert 60. sekund, mens Nexus er åben, også i bakken. Meddelelser aktiverer baggrundstjek og rapporterer problemer efter to fejlede tjek, derefter genoprettelse. Windows styrer varigheden; bakken bevarer fejlstatus. Uden baggrundstjek tjekkes kun den synlige statusvisning."), AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Theme.Muted, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            fields.Controls.Add(note);
            fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0);
            news.CheckedChanged += (sender, e) => feedChoices.Enabled = news.Checked;
            health.CheckedChanged += (sender, e) => servers.Enabled = health.Checked || background.Checked;
            background.CheckedChanged += (sender, e) => { if (!background.Checked) notifications.Checked = false; servers.Enabled = health.Checked || background.Checked; };
            notifications.CheckedChanged += (sender, e) => { if (notifications.Checked) background.Checked = true; };
            feedChoices.Enabled = news.Checked; servers.Enabled = health.Checked || background.Checked;
            Controls.Add(SlimScrolling.Wrap(fields));
        }
        internal void RebuildServers(IEnumerable<LoginServerProfile> profiles, IEnumerable<string> selected)
        {
            var ids = new HashSet<string>(selected ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (Control control in servers.Controls.Cast<Control>().ToArray()) control.Dispose();
            foreach (var profile in profiles)
            {
                var box = Check(profile.Name, ids.Contains(profile.Id), "healthServer-" + profile.Id); box.Tag = profile.Id; servers.Controls.Add(box);
            }
        }
        private static CheckBox Check(string text, bool value, string name) => new NexusCheckBox { Text = text, Checked = value, Name = name, AutoSize = true, Margin = new Padding(0, 4, 0, 5) };
    }

    internal sealed class ServerSidebar : UserControl
    {
        internal NewsPanel News { get; }
        internal HealthPanel Health { get; }
        internal NexusTabControl Tabs { get; private set; }
        internal ServerSidebar(bool preview)
        {
            Dock = DockStyle.Fill; Margin = Padding.Empty; BackColor = Theme.Surface;
            News = new NewsPanel(preview) { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Health = new HealthPanel(preview) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        }
        internal void Configure(LauncherSettings settings)
        {
            bool wasHealth = Tabs?.SelectedIndex == 1;
            SuspendLayout();
            News.Visible = false; Health.Visible = false;
            News.Parent = null; Health.Parent = null;
            if (Tabs != null) { Tabs.Dispose(); Tabs = null; }
            Controls.Clear();
            News.ApplySelectionPreferences(settings.NewsSourceIds);
            Health.Endpoints = HealthEndpoints.Clone(settings.HealthCheckEndpoints);
            Health.Configure(settings.LoginServers, settings.HealthServerIds);
            Health.SetBackgroundChecks(settings.BackgroundHealthChecks || settings.HealthTrayNotifications);
            News.RefreshLanguage(); Health.RefreshLanguage();
            if (settings.ShowServerNews && settings.ShowServerHealth)
            {
                Tabs = new NexusTabControl { Name = "serverSidebarTabs", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9F) };
                var news = new TabPage(L("Server news", "Servernyheder")) { BackColor = Theme.Surface };
                var health = new TabPage(L("Server health", "Serverstatus")) { BackColor = Theme.Surface };
                news.Controls.Add(News); health.Controls.Add(Health); Tabs.TabPages.Add(news); Tabs.TabPages.Add(health);
                Controls.Add(Tabs); if (wasHealth) Tabs.SelectedIndex = 1;
            }
            else if (settings.ShowServerNews) Controls.Add(News);
            else if (settings.ShowServerHealth) Controls.Add(Health);
            News.Visible = settings.ShowServerNews; Health.Visible = settings.ShowServerHealth;
            Visible = settings.ShowServerNews || settings.ShowServerHealth;
            ResumeLayout(true);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { News.Dispose(); Health.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class HealthPanel : UserControl
    {
        private readonly bool preview;
        private readonly ComboBox picker = new NexusComboBox();
        private readonly Label heading = new Label(), status = new Label(), serverName = new Label();
        private readonly Panel serverChoice = new Panel();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Dictionary<string, List<HealthResult[]>> history = new Dictionary<string, List<HealthResult[]>>();
        private CancellationTokenSource pending;
        private bool busy, disposed, deferredRender;
        private bool backgroundChecks, configuring;
        internal event Action<List<HealthSnapshot>> Checked;
        private DateTime lastAttempt = DateTime.MinValue;
        internal Func<LoginServerProfile, CancellationToken, Task<HealthResult[]>> Check = ServerHealth.CheckAsync;
        internal Dictionary<string, HealthEndpoints> Endpoints = new Dictionary<string, HealthEndpoints>();
        internal HealthPanel(bool preview)
        {
            this.preview = preview; Name = "serverHealthPanel"; BackColor = Theme.Surface; ForeColor = Theme.Foreground;
            Check = (server, cancellation) => ServerHealth.CheckConfiguredAsync(server, HealthEndpoints.Resolve(server, Endpoints), cancellation);
            Font = new Font("Segoe UI", 9F); Padding = new Padding(14);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            heading.Dock = DockStyle.Fill; heading.ForeColor = Theme.Accent; heading.TextAlign = ContentAlignment.MiddleLeft;
            picker.DropDownStyle = ComboBoxStyle.DropDownList; picker.Dock = DockStyle.Fill; picker.Margin = new Padding(0, 5, 0, 5);
            serverChoice.Dock = DockStyle.Fill; serverChoice.Margin = Padding.Empty;
            serverName.Name = "healthServerName"; serverName.Dock = DockStyle.Fill; serverName.ForeColor = Theme.Foreground;
            serverName.TextAlign = ContentAlignment.MiddleLeft;
            serverChoice.Controls.Add(picker); serverChoice.Controls.Add(serverName);
            picker.SelectedIndexChanged += (sender, e) => { if (!backgroundChecks) pending?.Cancel(); Render(); if (!configuring) _ = RefreshAsync(false); };
            cards.Dock = DockStyle.Fill; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false; cards.AutoScroll = true;
            cards.ClientSizeChanged += (sender, e) => { foreach (Control card in cards.Controls) card.Width = Math.Max(100, cards.ClientSize.Width - 4); };
            status.Dock = DockStyle.Fill; status.ForeColor = Theme.Muted;
            layout.Controls.Add(heading, 0, 0); layout.Controls.Add(serverChoice, 0, 1); layout.Controls.Add(SlimScrolling.Wrap(cards), 0, 2);
            layout.Controls.Add(status, 0, 3); Controls.Add(layout);
            timer.Tick += async (sender, e) => { if (!backgroundChecks && BackgroundUi.DropdownOpen()) return; if (deferredRender && Visible) Render(); if (Visible) UpdateStatus(); await RefreshAsync(false); };
            VisibleChanged += (sender, e) => { if (Visible) { timer.Start(); Render(); if (!configuring) _ = RefreshAsync(false); } else if (!backgroundChecks) { timer.Stop(); pending?.Cancel(); } };
            Load += async (sender, e) => { if (Visible) { timer.Start(); await RefreshAsync(false); } };
            RefreshLanguage();
        }
        internal void Configure(IEnumerable<LoginServerProfile> profiles, IEnumerable<string> selected)
        {
            configuring = true;
            string current = (picker.SelectedItem as LoginServerProfile)?.Id;
            var ids = new HashSet<string>(selected ?? new[] { "rebirth", "prk", "live", "live-2019" }, StringComparer.OrdinalIgnoreCase);
            pending?.Cancel(); picker.Items.Clear();
            foreach (var item in profiles.Where(item => ids.Contains(item.Id)))
                picker.Items.Add(new LoginServerProfile { Id = item.Id, Name = item.Name, Server = item.Server, Port = item.Port });
            picker.SelectedItem = picker.Items.Cast<LoginServerProfile>().FirstOrDefault(item => item.Id == current);
            if (picker.SelectedIndex < 0 && picker.Items.Count > 0) picker.SelectedIndex = 0;
            picker.Visible = picker.Items.Count > 1;
            serverName.Visible = picker.Items.Count == 1;
            serverName.Text = picker.Items.Count == 1 ? picker.Items[0].ToString() : "";
            serverChoice.Visible = picker.Items.Count > 0;
            var validKeys = new HashSet<string>(picker.Items.Cast<LoginServerProfile>().Select(item => ServerHealth.EndpointKey(item) + "|" + HealthEndpoints.Resolve(item, Endpoints).Key));
            foreach (string key in history.Keys.Where(key => !validKeys.Contains(key)).ToArray()) history.Remove(key);
            configuring = false;
            Render();
        }
        internal void SetBackgroundChecks(bool enabled)
        {
            if (backgroundChecks != enabled) { pending?.Cancel(); lastAttempt = DateTime.MinValue; }
            backgroundChecks = enabled;
            if (enabled || Visible) timer.Start(); else { timer.Stop(); pending?.Cancel(); }
        }
        internal void RefreshLanguage()
        {
            heading.Text = L("SERVER HEALTH", "SERVERSTATUS");
            Render();
        }
        internal async Task RefreshAsync(bool manual)
        {
            var server = picker.SelectedItem as LoginServerProfile;
            if (disposed || busy || configuring || (!backgroundChecks && (!Visible || !IsHandleCreated || BackgroundUi.DropdownOpen())) || server == null) return;
            var candidates = backgroundChecks ? picker.Items.Cast<LoginServerProfile>().ToArray() : new[] { server };
            var due = candidates.GroupBy(item => ServerHealth.EndpointKey(item) + "|" + HealthEndpoints.Resolve(item, Endpoints).Key).Select(group => group.First())
                .Where(item => manual || Samples(item).Count == 0 || DateTime.UtcNow - Samples(item).Last()[0].CheckedUtc >= TimeSpan.FromSeconds(60)).ToArray();
            if (due.Length == 0) return;
            // Global throttle also covers switching servers repeatedly.
            if (DateTime.UtcNow - lastAttempt < TimeSpan.FromSeconds(15)) return;
            lastAttempt = DateTime.UtcNow; busy = true;
            var cancellation = new CancellationTokenSource(); pending = cancellation;
            try
            {
                var completed = new List<HealthSnapshot>();
                foreach (var target in due)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var result = preview ? Preview(target) : await Check(target, cancellation.Token);
                    if (disposed || cancellation.IsCancellationRequested) return;
                    if (result == null || result.Length == 0) continue;
                    var samples = Samples(target);
                    samples.Add(result); if (samples.Count > 24) samples.RemoveAt(0);
                    completed.Add(new HealthSnapshot { Server = target, Scope = ServerHealth.EndpointKey(target) + "|" + HealthEndpoints.Resolve(target, Endpoints).Key, Results = result });
                }
                if (completed.Count > 0) Checked?.Invoke(completed);
                if (Visible) Render(); else deferredRender = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Failed observation is not evidence of a service outage. Retry on the timer. */ }
            finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); busy = false; if (!disposed) UpdateStatus(); }
        }
        private List<HealthResult[]> Samples(LoginServerProfile server)
        {
            string key = ServerHealth.EndpointKey(server) + "|" + HealthEndpoints.Resolve(server, Endpoints).Key;
            if (!history.TryGetValue(key, out var result))
            {
                history[key] = result = new List<HealthResult[]>();
            }
            return result;
        }
        private HealthResult[] Preview(LoginServerProfile server)
        {
            var endpoints = HealthEndpoints.Resolve(server, Endpoints);
            var checks = new[] { endpoints.Login, endpoints.Zone, endpoints.Chat };
            var keys = new[] { "login", "zone", "chat" };
            var results = checks.Select((endpoint, index) => endpoint.Enabled && endpoint.Valid
                ? new HealthResult { Key = keys[index], State = HealthState.Reachable, Detail = endpoint.Host + ":" + endpoint.Port + " · TCP", Milliseconds = 18, CheckedUtc = DateTime.UtcNow }
                : ServerHealth.Unknown(keys[index], L("Check disabled or not configured.", "Tjek slået fra eller ikke konfigureret."), DateTime.UtcNow)).ToList();
            string kind = ServerHealth.Kind(server);
            results.Add(new HealthResult { Key = "report", CheckedUtc = DateTime.UtcNow, State = kind == "rebirth" || kind == "prk" ? HealthState.ReportedOnline : HealthState.Unknown,
                Detail = kind == "rebirth" ? L("Login: online · Zones: online · Chat: online · Accounts: online", "Login: online · Zoner: online · Chat: online · Konti: online")
                    : kind == "prk" ? L("Listed by PRK. No per-service status or source timestamp supplied.", "Rapporteret af PRK. Ingen særskilt servicestatus eller tidsstempel fra kilden.")
                    : L("No verified public service report.", "Ingen verificeret offentlig servicestatus.") });
            return results.ToArray();
        }
        private void Render()
        {
            if (disposed) return;
            if (!Visible) { deferredRender = true; return; }
            if (BackgroundUi.DropdownOpen()) { deferredRender = true; return; }
            deferredRender = false;
            cards.SuspendLayout(); foreach (Control control in cards.Controls.Cast<Control>().ToArray()) control.Dispose();
            var server = picker.SelectedItem as LoginServerProfile;
            if (server == null) cards.Controls.Add(new Label { Text = L("Choose health servers in Settings → Sidebar.", "Vælg statusservere i Indstillinger → Sidepanel."), AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = Theme.Muted });
            else
            {
                var samples = Samples(server);
                foreach (string key in new[] { "login", "zone", "chat", "report" })
                {
                    var result = samples.LastOrDefault()?.FirstOrDefault(value => value.Key == key) ?? ServerHealth.Unknown(key, L("Not checked yet.", "Ikke tjekket endnu."), DateTime.MinValue);
                    var card = new HealthCard(result, samples.Select(sample => sample.FirstOrDefault(value => value.Key == key)).Where(value => value != null).ToArray());
                    card.Width = Math.Max(100, cards.ClientSize.Width - 4); cards.Controls.Add(card);
                }
            }
            cards.ResumeLayout(true); UpdateStatus();
        }
        private void UpdateStatus()
        {
            if (BackgroundUi.DropdownOpen()) return;
            var server = picker.SelectedItem as LoginServerProfile;
            var sample = server == null ? null : Samples(server).LastOrDefault();
            status.Text = busy ? L("Checking…", "Tjekker…") : sample == null ? L("Waiting for a check", "Venter på et tjek")
                : (preview ? L("Example data", "Eksempeldata") : L("Checked ", "Tjekket ") + sample[0].CheckedUtc.ToLocalTime().ToString("HH:mm:ss")) + (backgroundChecks ? L(" · background · every 60s", " · baggrund · hvert 60. sekund") : L(" · every 60s while visible", " · hvert 60. sekund når synlig"));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; timer.Stop(); timer.Dispose(); pending?.Cancel(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class HealthCard : Control
    {
        private readonly HealthResult result;
        private readonly HealthResult[] history;
        private int measuredWidth;
        internal HealthCard(HealthResult result, HealthResult[] history)
        {
            this.result = result; this.history = history; Height = 116; Margin = new Padding(0, 0, 0, 8);
            BackColor = Theme.SurfaceRaised; ForeColor = Theme.Foreground; Font = new Font("Segoe UI", 9F);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AccessibleName = Title(result.Key) + ": " + Caption(result.State) + ". " + result.Detail;
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (result == null || Width == measuredWidth) return;
            measuredWidth = Width;
            int inset = NexusChrome.Scale(this, 10), line = Font.Height + 4;
            int detailHeight = TextRenderer.MeasureText(result.Detail, Font, new Size(Math.Max(50, Width - inset * 2), int.MaxValue), TextFormatFlags.WordBreak).Height;
            Height = Math.Max(NexusChrome.Scale(this, 94), inset * 2 + line * 2 + detailHeight + NexusChrome.Scale(this, 18));
        }
        private static string Title(string key) => key == "login" ? L("Login connection", "Loginforbindelse") : key == "zone" ? L("World / zone connection", "Verden / zoneforbindelse") : key == "chat" ? L("Chat connection", "Chatforbindelse") : L("Official service report", "Officiel servicestatus");
        private static string Caption(HealthState state) => state == HealthState.Reachable ? L("Reachable", "Tilgængelig") : state == HealthState.Unreachable ? L("Unreachable", "Utilgængelig") : state == HealthState.ReportedOnline ? L("Reported online", "Rapporteret online") : state == HealthState.ReportedIssue ? L("Reported issue", "Rapporteret problem") : L("Unknown", "Ukendt");
        private static Color ColorFor(HealthState state) => state == HealthState.Reachable || state == HealthState.ReportedOnline ? Color.FromArgb(85, 190, 139) : state == HealthState.Unreachable || state == HealthState.ReportedIssue ? Theme.Amber : Theme.Muted;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int inset = NexusChrome.Scale(this, 10), line = Font.Height + 4;
            bool stale = result.CheckedUtc != DateTime.MinValue && DateTime.UtcNow - result.CheckedUtc > TimeSpan.FromSeconds(150);
            HealthState state = stale ? HealthState.Unknown : result.State;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Title(result.Key), Font, new Rectangle(inset, inset, Width - inset * 2, line), Theme.Foreground, TextFormatFlags.EndEllipsis);
            string label = stale ? L("Outdated — waiting for next check", "Forældet — afventer næste tjek") : Caption(state) + (result.Milliseconds.HasValue ? " · " + result.Milliseconds + " ms" : "");
            TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(inset, inset + line, Width - inset * 2, line), ColorFor(state), TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, result.Detail, Font, new Rectangle(inset, inset + line * 2, Width - inset * 2, line * 3), Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            int barWidth = Math.Max(2, (Width - inset * 2) / 24 - 2), barHeight = NexusChrome.Scale(this, 8);
            for (int i = 0; i < 24; i++)
            {
                int index = i - (24 - history.Length);
                using (var brush = new SolidBrush(index < 0 ? Theme.Border : ColorFor(history[index].State)))
                    e.Graphics.FillRectangle(brush, inset + i * (barWidth + 2), Height - inset - barHeight, barWidth, barHeight);
            }
        }
    }
}
