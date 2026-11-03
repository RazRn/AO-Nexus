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
            news = Check(AOLauncher.Localization.Texts.Get("Text_Show_Server_news_AE2AF7BADF88"), settings.ShowServerNews, "showServerNews");
            fields.Controls.Add(news);
            var feedChoices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(18, 0, 0, 14) };
            foreach (var source in ServerNews.Sources)
            {
                var box = Check(source.Name, ServerNews.NormalizeSourceIds(settings.NewsSourceIds).Contains(source.Id), "newsSource-" + source.Id);
                sources.Add(source.Id, box); feedChoices.Controls.Add(box);
            }
            fields.Controls.Add(feedChoices);
            health = Check(AOLauncher.Localization.Texts.Get("Text_Show_Server_health_A2C7B2E3B7E1"), settings.ShowServerHealth, "showServerHealth");
            fields.Controls.Add(health);
            background = Check(AOLauncher.Localization.Texts.Get("Text_Check_selected_servers_in_the_background_E9C695B964D5"), settings.BackgroundHealthChecks || settings.HealthTrayNotifications, "backgroundHealthChecks");
            notifications = Check(AOLauncher.Localization.Texts.Get("Text_Tray_notifications_for_outages_and_recovery_F8C7CF877E21"), settings.HealthTrayNotifications, "healthTrayNotifications");
            fields.Controls.Add(background); fields.Controls.Add(notifications);
            servers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(18, 0, 0, 14) };
            fields.Controls.Add(servers);
            RebuildServers(settings.LoginServers, settings.HealthServerIds ?? new List<string> { "rebirth", "prk", "live", "live-2019" });
            var note = new Label { Text = AOLauncher.Localization.Texts.Get("Text_Background_checks_run_every_60_seconds_095E0BA50C36"), AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Theme.Muted, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
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
            // The reusable panels are detached; release replaced stacks/cards and handles.
            foreach (Control child in Controls.Cast<Control>().ToArray()) child.Dispose();
            Controls.Clear();
            News.ApplySelectionPreferences(settings.NewsSourceIds);
            Health.Endpoints = HealthEndpoints.Clone(settings.HealthCheckEndpoints);
            Health.Configure(settings.LoginServers, settings.HealthServerIds);
            Health.SetBackgroundChecks(settings.BackgroundHealthChecks || settings.HealthTrayNotifications);
            News.RefreshLanguage(); Health.RefreshLanguage();
            if (settings.ShowServerNews && settings.ShowServerHealth)
            {
                // Server status above news, both visible (main window design), instead of tabs.
                BackColor = Theme.Background;
                var stack = new TableLayoutPanel { Name = "serverSidebarStack", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
                stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                stack.RowStyles.Add(new RowStyle(SizeType.Percent, 46)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
                var healthCard = new NexusPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(1), BackColor = Theme.Surface };
                var newsCard = new NexusPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(1), BackColor = Theme.Surface };
                healthCard.Controls.Add(Health); newsCard.Controls.Add(News);
                stack.Controls.Add(healthCard, 0, 0); stack.Controls.Add(newsCard, 0, 1);
                Controls.Add(stack);
            }
            else if (settings.ShowServerNews) { BackColor = Theme.Surface; Controls.Add(News); }
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
        private readonly Label heading = new Label(), serverName = new Label();
        private readonly ToolTip tips=new ToolTip();
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
            Font = new Font("Segoe UI", 9F); Padding = new Padding(10,6,10,8);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            heading.Dock = DockStyle.Fill; heading.ForeColor = Theme.Accent; heading.TextAlign = ContentAlignment.MiddleLeft;
            picker.DropDownStyle = ComboBoxStyle.DropDownList; picker.Dock = DockStyle.Fill; picker.Margin = new Padding(0, 5, 0, 5);
            serverChoice.Dock = DockStyle.Fill; serverChoice.Margin = Padding.Empty;
            serverName.Name = "healthServerName"; serverName.Dock = DockStyle.Fill; serverName.ForeColor = Theme.Foreground;
            serverName.TextAlign = ContentAlignment.MiddleLeft;
            serverChoice.Controls.Add(picker); serverChoice.Controls.Add(serverName);
            picker.SelectedIndexChanged += (sender, e) => { if (!backgroundChecks) pending?.Cancel(); Render(); if (!configuring) _ = RefreshAsync(false); };
            cards.Dock = DockStyle.Fill; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false; cards.AutoScroll = true;
            cards.ClientSizeChanged += (sender, e) => { foreach (Control card in cards.Controls) card.Width = Math.Max(100, cards.ClientSize.Width - 4); };
            var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,112));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.Controls.Add(heading,0,0);header.Controls.Add(serverChoice,1,0);
            layout.Controls.Add(header,0,0);layout.Controls.Add(SlimScrolling.Wrap(cards),0,1);Controls.Add(layout);
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
            heading.Text = AOLauncher.Localization.Texts.Get("Text_SERVER_HEALTH_5D3BDAE22723");
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
                : ServerHealth.Unknown(keys[index], AOLauncher.Localization.Texts.Get("Text_Check_disabled_or_not_configured_74349205A58C"), DateTime.UtcNow)).ToList();
            string kind = ServerHealth.Kind(server);
            results.Add(new HealthResult { Key = "report", CheckedUtc = DateTime.UtcNow, State = kind == "rebirth" || kind == "prk" ? HealthState.ReportedOnline : HealthState.Unknown,
                Detail = kind == "rebirth" ? AOLauncher.Localization.Texts.Get("Text_Login_online_Zones_online_Chat_online_39BE23DE11A7")
                    : kind == "prk" ? AOLauncher.Localization.Texts.Get("Text_Listed_by_PRK_No_per_service_124D6C17AB74")
                    : AOLauncher.Localization.Texts.Get("Text_No_verified_public_service_report_1DBBAF184B50") });
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
            if (server == null) cards.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_Choose_health_servers_in_Settings_Sidebar_926BBF5B456D"), AutoSize = true, MaximumSize = new Size(280, 0), ForeColor = Theme.Muted });
            else
            {
                var samples = Samples(server);
                foreach (string key in new[] { "login", "zone", "chat", "report" })
                {
                    var result = samples.LastOrDefault()?.FirstOrDefault(value => value.Key == key) ?? ServerHealth.Unknown(key, AOLauncher.Localization.Texts.Get("Text_Not_checked_yet_230D01774B7D"), DateTime.MinValue);
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
            string detail=busy?AOLauncher.Localization.Texts.Get("Text_Checking_B15323E4F6AD"):sample==null?AOLauncher.Localization.Texts.Get("Text_Waiting_for_a_check_C6195F06E6D2"):
                AOLauncher.Localization.Texts.Get("Text_Last_checked_B74D740E8896")+sample[0].CheckedUtc.ToLocalTime().ToString("HH:mm:ss");
            tips.SetToolTip(heading,detail);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; tips.Dispose(); timer.Stop(); timer.Dispose(); pending?.Cancel(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class HealthCard : Control
    {
        private readonly HealthResult result;
        private readonly HealthResult[] history;
        private int measuredWidth;
        private readonly ToolTip detailsTip=new ToolTip();
        internal HealthCard(HealthResult result, HealthResult[] history)
        {
            this.result = result; this.history = history; Height = 50; Margin = new Padding(0, 0, 0, 4);
            BackColor = Theme.SurfaceRaised; ForeColor = Theme.Foreground; Font = new Font("Segoe UI", 9F);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AccessibleName = Title(result.Key) + ": " + Caption(result.State) + ". " + result.Detail;
            detailsTip.SetToolTip(this,AccessibleName);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (result == null || Width == measuredWidth) return;
            measuredWidth = Width;
            int inset = NexusChrome.Scale(this, 8), line = Font.Height + 4;
            Height = inset * 2 + line + NexusChrome.Scale(this,8);
        }
        protected override void Dispose(bool disposing){if(disposing)detailsTip.Dispose();base.Dispose(disposing);}
        private static string Title(string key) => key == "login" ? "Login" : key == "zone" ? AOLauncher.Localization.Texts.Get("Text_World_zone_8210E875436B") : key == "chat" ? "Chat" : AOLauncher.Localization.Texts.Get("Text_Official_report_741785D5A656");
        private static string Caption(HealthState state) => state == HealthState.Reachable ? AOLauncher.Localization.Texts.Get("Text_Reachable_54DB153474A9") : state == HealthState.Unreachable ? AOLauncher.Localization.Texts.Get("Text_Unreachable_263A5CE5D6FC") : state == HealthState.ReportedOnline ? AOLauncher.Localization.Texts.Get("Text_Reported_online_E847C0DF1229") : state == HealthState.ReportedIssue ? AOLauncher.Localization.Texts.Get("Text_Reported_issue_A629ABE96E16") : AOLauncher.Localization.Texts.Get("Text_Unknown_D8D7AE0368FB");
        private static Color ColorFor(HealthState state) => state == HealthState.Reachable || state == HealthState.ReportedOnline ? Color.FromArgb(85, 190, 139) : state == HealthState.Unreachable || state == HealthState.ReportedIssue ? Theme.Amber : Theme.Muted;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int inset = NexusChrome.Scale(this, 8), line = Font.Height + 4;
            bool stale = result.CheckedUtc != DateTime.MinValue && DateTime.UtcNow - result.CheckedUtc > TimeSpan.FromSeconds(150);
            HealthState state = stale ? HealthState.Unknown : result.State;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            string label = stale ? AOLauncher.Localization.Texts.Get("Text_Outdated_waiting_for_next_check_7A92103DA916") : Caption(state) + (result.Milliseconds.HasValue ? " · " + result.Milliseconds + " ms" : "");
            string title=Title(result.Key)+" · ";
            int titleWidth=TextRenderer.MeasureText(e.Graphics,title,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(e.Graphics,title,Font,new Rectangle(inset,inset,titleWidth,line),Theme.Foreground,TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics,label,Font,new Rectangle(inset+titleWidth,inset,Math.Max(1,Width-inset*2-titleWidth),line),ColorFor(state),TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            int barWidth = Math.Max(2, (Width - inset * 2) / 24 - 2), barHeight = NexusChrome.Scale(this, 6);
            for (int i = 0; i < 24; i++)
            {
                int index = i - (24 - history.Length);
                using (var brush = new SolidBrush(index < 0 ? Theme.Border : ColorFor(history[index].State)))
                    e.Graphics.FillRectangle(brush, inset + i * (barWidth + 2), Height - inset - barHeight, barWidth, barHeight);
            }
        }
    }
}
