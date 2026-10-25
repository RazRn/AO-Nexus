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
        private readonly CheckBox news, health;
        private readonly Dictionary<string, CheckBox> sources = new Dictionary<string, CheckBox>();
        private readonly FlowLayoutPanel servers;
        internal bool ShowNews => news.Checked;
        internal bool ShowHealth => health.Checked;
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
            servers = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(18, 0, 0, 14) };
            fields.Controls.Add(servers);
            RebuildServers(settings.LoginServers, settings.HealthServerIds ?? new List<string> { "rebirth", "prk", "live", "live-2019" });
            var note = new Label { Text = L("When both are enabled, they share the sidebar as tabs. Health checks run only while the health view is visible. Turn both off for more account space.", "Når begge er slået til, vises de som faner. Status tjekkes kun, mens statusvisningen er synlig. Slå begge fra for mere plads til konti."), AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = Theme.Muted, Dock = DockStyle.Top, Margin = new Padding(0, 12, 0, 8) };
            fields.Controls.Add(note);
            fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0);
            news.CheckedChanged += (sender, e) => feedChoices.Enabled = news.Checked;
            health.CheckedChanged += (sender, e) => servers.Enabled = health.Checked;
            feedChoices.Enabled = news.Checked; servers.Enabled = health.Checked;
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
            Health.Configure(settings.LoginServers, settings.HealthServerIds);
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
        private readonly Button refresh = Theme.Button("Refresh");
        private readonly Label heading = new Label(), status = new Label(), guide = new Label();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Dictionary<string, List<HealthResult[]>> history = new Dictionary<string, List<HealthResult[]>>();
        private CancellationTokenSource pending;
        private bool busy, disposed;
        private DateTime lastAttempt = DateTime.MinValue;
        internal Func<LoginServerProfile, CancellationToken, Task<HealthResult[]>> Check = ServerHealth.CheckAsync;
        internal HealthPanel(bool preview)
        {
            this.preview = preview; Name = "serverHealthPanel"; BackColor = Theme.Surface; ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 9F); Padding = new Padding(14);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            heading.Dock = DockStyle.Fill; heading.ForeColor = Theme.Accent; heading.TextAlign = ContentAlignment.MiddleLeft;
            refresh.Dock = DockStyle.Fill; refresh.MinimumSize = Size.Empty; refresh.Margin = Padding.Empty; refresh.AutoSize = false;
            refresh.Click += async (sender, e) => await RefreshAsync(true);
            header.Controls.Add(heading, 0, 0); header.Controls.Add(refresh, 1, 0);
            picker.DropDownStyle = ComboBoxStyle.DropDownList; picker.Dock = DockStyle.Fill; picker.Margin = new Padding(0, 5, 0, 5);
            ((NexusComboBox)picker).MatchHeight(refresh);
            picker.SelectedIndexChanged += (sender, e) => { pending?.Cancel(); Render(); _ = RefreshAsync(false); };
            cards.Dock = DockStyle.Fill; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false; cards.AutoScroll = true;
            cards.ClientSizeChanged += (sender, e) => { foreach (Control card in cards.Controls) card.Width = Math.Max(100, cards.ClientSize.Width - 4); };
            status.Dock = DockStyle.Fill; status.ForeColor = Theme.Muted;
            guide.Dock = DockStyle.Fill; guide.ForeColor = Theme.Muted;
            layout.Controls.Add(header, 0, 0); layout.Controls.Add(picker, 0, 1); layout.Controls.Add(SlimScrolling.Wrap(cards), 0, 2);
            layout.Controls.Add(status, 0, 3); layout.Controls.Add(guide, 0, 4); Controls.Add(layout);
            timer.Tick += async (sender, e) => { UpdateStatus(); await RefreshAsync(false); };
            VisibleChanged += (sender, e) => { if (Visible) { timer.Start(); Render(); _ = RefreshAsync(false); } else { timer.Stop(); pending?.Cancel(); } };
            Load += async (sender, e) => { if (Visible) { timer.Start(); await RefreshAsync(false); } };
            RefreshLanguage();
        }
        internal void Configure(IEnumerable<LoginServerProfile> profiles, IEnumerable<string> selected)
        {
            string current = (picker.SelectedItem as LoginServerProfile)?.Id;
            var ids = new HashSet<string>(selected ?? new[] { "rebirth", "prk", "live", "live-2019" }, StringComparer.OrdinalIgnoreCase);
            pending?.Cancel(); picker.Items.Clear();
            foreach (var item in profiles.Where(item => ids.Contains(item.Id)))
                picker.Items.Add(new LoginServerProfile { Id = item.Id, Name = item.Name, Server = item.Server, Port = item.Port });
            picker.SelectedItem = picker.Items.Cast<LoginServerProfile>().FirstOrDefault(item => item.Id == current);
            if (picker.SelectedIndex < 0 && picker.Items.Count > 0) picker.SelectedIndex = 0;
            Render();
        }
        internal void RefreshLanguage()
        {
            heading.Text = L("SERVER HEALTH", "SERVERSTATUS"); refresh.Text = L("Refresh", "Opdater");
            guide.Text = L("TCP shows reachability from your PC, not a successful login. Unreachable can mean a local connection problem. Official reports are separate.\r\n\r\nBars: recent checks this session. Checks pause when hidden.", "TCP viser adgang fra din pc, ikke et vellykket login. Manglende adgang kan skyldes din forbindelse. Officielle rapporter vises separat.\r\n\r\nSøjler: seneste tjek i denne session. Pause når skjult.");
            Render();
        }
        internal async Task RefreshAsync(bool manual)
        {
            var server = picker.SelectedItem as LoginServerProfile;
            if (disposed || busy || !Visible || !IsHandleCreated || server == null) return;
            var samples = Samples(server);
            DateTime last = samples.Count == 0 ? DateTime.MinValue : samples.Last()[0].CheckedUtc;
            if (!manual && DateTime.UtcNow - last < TimeSpan.FromSeconds(60)) return;
            // Global throttle also covers switching server repeatedly or clicking Refresh.
            if (DateTime.UtcNow - lastAttempt < TimeSpan.FromSeconds(15)) return;
            lastAttempt = DateTime.UtcNow; busy = true; refresh.Enabled = false;
            var cancellation = new CancellationTokenSource(); pending = cancellation;
            try
            {
                var result = preview ? Preview(server) : await Check(server, cancellation.Token);
                if (disposed || cancellation.IsCancellationRequested) return;
                samples.Add(result); if (samples.Count > 24) samples.RemoveAt(0);
                Render();
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); busy = false; if (!disposed) UpdateStatus(); }
        }
        private List<HealthResult[]> Samples(LoginServerProfile server)
        {
            string key = ServerHealth.EndpointKey(server);
            if (!history.TryGetValue(key, out var result))
            {
                if (history.Count >= 32) history.Remove(history.Keys.First());
                history[key] = result = new List<HealthResult[]>();
            }
            return result;
        }
        private static HealthResult[] Preview(LoginServerProfile server) => new[] {
            new HealthResult { Key = "login", State = HealthState.Reachable, Detail = server.Server + ":" + server.Port + " · TCP", Milliseconds = 18, CheckedUtc = DateTime.UtcNow },
            new HealthResult { Key = "zone", State = HealthState.Reachable, Detail = "2.24.96.30:7501 · TCP", Milliseconds = 19, CheckedUtc = DateTime.UtcNow },
            new HealthResult { Key = "chat", State = HealthState.Reachable, Detail = "2.24.96.30:7012 · TCP", Milliseconds = 17, CheckedUtc = DateTime.UtcNow },
            new HealthResult { Key = "report", State = HealthState.ReportedOnline, Detail = L("Login: online · Zones: online · Chat: online · Accounts: online", "Login: online · Zoner: online · Chat: online · Konti: online"), CheckedUtc = DateTime.UtcNow } };
        private void Render()
        {
            if (disposed) return;
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
            var server = picker.SelectedItem as LoginServerProfile;
            refresh.Enabled = server != null && !busy && DateTime.UtcNow - lastAttempt >= TimeSpan.FromSeconds(15);
            var sample = server == null ? null : Samples(server).LastOrDefault();
            status.Text = busy ? L("Checking…", "Tjekker…") : sample == null ? L("Waiting for a check", "Venter på et tjek")
                : (preview ? L("Example data", "Eksempeldata") : L("Checked ", "Tjekket ") + sample[0].CheckedUtc.ToLocalTime().ToString("HH:mm:ss")) + L(" · every 60s while visible", " · hvert 60. sekund når synlig");
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
            string label = stale ? L("Outdated — refresh to check", "Forældet — opdater for at tjekke") : Caption(state) + (result.Milliseconds.HasValue ? " · " + result.Milliseconds + " ms" : "");
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
