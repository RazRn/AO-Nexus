using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AOLauncher.Localization;

namespace AOLauncher
{
    internal sealed class NewsPanel : UserControl
    {
        private readonly bool offlinePreview;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Label heading = new Label();
        private readonly ToolTip tips=new ToolTip();
        private readonly Label status = new Label();
        private readonly Button refresh = new Button();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private readonly LinkLabel sourceLink = new LinkLabel();
        private readonly System.Windows.Forms.Timer automaticRefresh = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer interactionRefresh = new System.Windows.Forms.Timer { Interval = 250 };
        private bool deferredRender, deferredRefresh;
        private bool InteractionOpen => BackgroundUi.DropdownOpen() || ownedMenus.Any(menu => !menu.IsDisposed && menu.Visible);
        private readonly Font headingFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        private NewsSnapshot[] snapshots;
        private bool refreshing;
        private bool started;
        private bool resourcesDisposed;
        private bool applyingPreferences;
        private readonly HashSet<string> selectedSources = new HashSet<string>(ServerNews.Sources.Select(source => source.Id), StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ContextMenuStrip> ownedMenus = new HashSet<ContextMenuStrip>();

        public event EventHandler SelectionPreferencesChanged;
        public string[] SelectedSourceIds => ServerNews.Sources.Select(source => source.Id).Concat(new[] { "custom" }).Where(selectedSources.Contains).ToArray();

        public NewsPanel(bool offlinePreview = false)
        {
            this.offlinePreview = offlinePreview;
            snapshots = offlinePreview ? ServerNews.PreviewSnapshots() : new NewsSnapshot[0];
            BackColor = Theme.Surface;
            ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(10,6,10,8);
            Name = "serverNewsPanel";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            heading.Dock = DockStyle.Fill; heading.Font = headingFont; heading.ForeColor = Theme.Accent; heading.Margin = Padding.Empty;
            heading.TextAlign = ContentAlignment.MiddleLeft;
            var header = new TableLayoutPanel { Name = "newsHeader", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 4) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            refresh.Dock = DockStyle.Fill; refresh.FlatStyle = FlatStyle.Flat; refresh.FlatAppearance.BorderColor = Theme.Border;
            refresh.BackColor = Theme.SurfaceRaised; refresh.ForeColor = Theme.Accent; refresh.Margin = Padding.Empty; refresh.Cursor = Cursors.Hand; refresh.Name = "refreshNews";
            refresh.Click += async (sender, args) => await RefreshNewsAsync();
            header.Controls.Add(heading, 0, 0); header.Controls.Add(refresh, 1, 0);
            cards.Dock = DockStyle.Fill; cards.AutoScroll = true; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false;
            cards.Margin = Padding.Empty; cards.Padding = Padding.Empty; cards.BackColor = Theme.Surface; cards.Name = "newsCards";
            cards.ClientSizeChanged += (sender, args) => ResizeCards();
            status.AutoSize=true;status.Dock = DockStyle.Fill; status.ForeColor = Theme.Muted; status.TextAlign = ContentAlignment.MiddleLeft; status.Margin = new Padding(0, 3, 0, 0);
            sourceLink.Dock = DockStyle.Fill; sourceLink.LinkColor = Theme.Accent; sourceLink.ActiveLinkColor = Theme.Amber; sourceLink.VisitedLinkColor = Theme.Accent;
            sourceLink.Margin = Padding.Empty; sourceLink.TextAlign = ContentAlignment.MiddleLeft; sourceLink.LinkClicked += (sender, args) => OpenSource();
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(SlimScrolling.Wrap(cards), 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(sourceLink, 0, 4);
            Controls.Add(layout);
            automaticRefresh.Interval = 15 * 60 * 1000;
            automaticRefresh.Tick += async (sender, args) => await RefreshNewsAsync();
            interactionRefresh.Tick += async (sender, args) =>
            {
                if (!Visible || IsDisposed) { interactionRefresh.Stop(); return; }
                if (InteractionOpen) return;
                interactionRefresh.Stop();
                if (deferredRender) Render();
                if (deferredRefresh) { deferredRefresh = false; await RefreshNewsAsync(); }
            };
            RefreshLanguage();
            Load += (sender, e) => ActivityChanged();
            VisibleChanged += (sender, e) => ActivityChanged();
        }

        private CancellationTokenSource refreshCancellation;
        private DateTime lastRefreshUtc = DateTime.MinValue;
        private void ActivityChanged()
        {
            if (!Visible) { automaticRefresh.Stop(); interactionRefresh.Stop(); refreshCancellation?.Cancel(); return; }
            if (deferredRender) Render();
            if (offlinePreview || !IsHandleCreated || IsDisposed) return;
            started = true; automaticRefresh.Start();
            if (DateTime.UtcNow - lastRefreshUtc >= TimeSpan.FromMinutes(15)) _ = RefreshNewsAsync();
        }

        private static CultureInfo Culture => CultureInfo.GetCultureInfo(Texts.CurrentLanguage == "da" ? "da-DK" : "en-GB");

        public void RefreshLanguage()
        {
            heading.Text = AOLauncher.Localization.Texts.Get("Text_SERVER_NEWS_45AA7C2C7ED5");
            refresh.Text = AOLauncher.Localization.Texts.Get("Text_Refresh_92873B0EC430");
            refresh.AccessibleName = AOLauncher.Localization.Texts.Get("Text_Refresh_server_news_E6DEA603A784");
            Render();
        }

        public void ApplySelectionPreferences(IEnumerable<string> sourceIds)
        {
            var normalized = ServerNews.NormalizeSourceIds(sourceIds);
            if (!selectedSources.SetEquals(normalized)) { refreshCancellation?.Cancel(); lastRefreshUtc = DateTime.MinValue; }
            applyingPreferences = true;
            try
            {
                selectedSources.Clear();
                selectedSources.UnionWith(normalized);
                Render();
            }
            finally { applyingPreferences = false; }
        }

        public void SetServerFilters(IEnumerable<string> sourceIds)
        {
            var values = ServerNews.NormalizeSourceIds(sourceIds);
            if (selectedSources.SetEquals(values)) return;
            selectedSources.Clear(); selectedSources.UnionWith(values);
            Render();
            if (!applyingPreferences) SelectionPreferencesChanged?.Invoke(this, EventArgs.Empty);
            if (started && !offlinePreview && ServerNews.Sources.Any(source => selectedSources.Contains(source.Id) &&
                !snapshots.Any(snapshot => snapshot.SourceId == source.Id && snapshot.FetchedUtc != DateTime.MinValue)))
                _ = RefreshNewsAsync();
        }

        public void SetServerFilter(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId) || string.Equals(serverId.Trim(), "all", StringComparison.OrdinalIgnoreCase)) { SetServerFilters(null); return; }
            string id = serverId.Trim().ToLowerInvariant();
            if (id == "live-2019") id = "live";
            SetServerFilters(new[] { ServerNews.Sources.Any(source => source.Id == id) ? id : "custom" });
        }

        public async Task RefreshNewsAsync()
        {
            if (refreshing || IsDisposed || lifetime.IsCancellationRequested || !Visible) return;
            if (InteractionOpen) { deferredRefresh = true; interactionRefresh.Start(); return; }
            if (offlinePreview || !ServerNews.Sources.Any(source => selectedSources.Contains(source.Id))) { Render(); return; }
            refreshing = true;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            refreshCancellation = cancellation;
            refresh.Enabled = false;
            UpdateStatus();
            try
            {
                snapshots = await Task.WhenAll(ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)).Select(source => ServerNews.FetchAsync(source, ServerNews.CacheDirectory, cancellation.Token)));
                lastRefreshUtc = DateTime.UtcNow;
                if (!IsDisposed && !lifetime.IsCancellationRequested) Render();
            }
            catch (OperationCanceledException) { }
            finally
            {
                refreshing = false;
                if (ReferenceEquals(refreshCancellation, cancellation)) refreshCancellation = null;
                cancellation.Dispose();
                if (!IsDisposed && !lifetime.IsCancellationRequested)
                {
                    if (InteractionOpen) { deferredRender = true; interactionRefresh.Start(); }
                    else { refresh.Enabled = ServerNews.Sources.Any(source => selectedSources.Contains(source.Id)); UpdateStatus(); }
                }
            }
        }

        private void Render()
        {
            if (IsDisposed) return;
            if (InteractionOpen) { deferredRender = true; interactionRefresh.Start(); return; }
            deferredRender = false;
            refresh.Enabled = !refreshing && ServerNews.Sources.Any(source => selectedSources.Contains(source.Id));
            cards.SuspendLayout();
            foreach (Control child in cards.Controls.Cast<Control>().ToArray()) child.Dispose();
            cards.Controls.Clear();
            var selected = ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)).ToArray();
            var visible = snapshots.Where(snapshot => selectedSources.Contains(snapshot.SourceId)).ToList();
            if (selectedSources.Count == 0)
            {
                AddMessage(AOLauncher.Localization.Texts.Get("Text_No_news_sources_selected_8B02D1B163C0"), AOLauncher.Localization.Texts.Get("Text_Choose_news_sources_in_Settings_Sidebar_418B6B549E36"));
            }
            else if (selected.Length > 0)
            {
                // Lead with one article per publisher, so the busiest server cannot hide the others.
                var first = visible.Select(snapshot => snapshot.Articles.FirstOrDefault()).Where(article => article != null).OrderByDescending(article => article.PublishedUtc).ToList();
                var remaining = visible.SelectMany(snapshot => snapshot.Articles.Skip(1)).OrderByDescending(article => article.PublishedUtc);
                foreach (var article in first.Concat(remaining)) AddArticle(article, visible.First(snapshot => snapshot.SourceId == article.SourceId));
                foreach (var source in selected)
                {
                    var state = visible.FirstOrDefault(snapshot => snapshot.SourceId == source.Id);
                    if (state != null && state.Unavailable && state.Articles.Count == 0)
                        AddMessage(source.Name, AOLauncher.Localization.Texts.Get("Text_News_is_temporarily_unavailable_Use_the_3ED628F55D3F"), source);
                }
                if (cards.Controls.Count == 0) AddMessage(AOLauncher.Localization.Texts.Get("Text_Connecting_to_Rubi_Ka_6A6D531185F9"), AOLauncher.Localization.Texts.Get("Text_Fetching_the_latest_published_articles_You_4912A2D2DBFF"));
            }
            if (selectedSources.Contains("custom"))
                AddMessage(AOLauncher.Localization.Texts.Get("Text_Your_own_corner_of_Rubi_Ka_3F0AB8979B47"), AOLauncher.Localization.Texts.Get("Text_Local_and_custom_servers_do_not_D3AD91E6D7C7"));
            sourceLink.Visible = selected.Length > 0;
            sourceLink.Text = selected.Length > 1 ? AOLauncher.Localization.Texts.Get("Text_Open_server_websites_DD5974FD1EAA") : AOLauncher.Localization.Texts.Get("Text_Visit_official_news_E5A4ED0D897A");
            refresh.Enabled = !refreshing && selected.Length > 0;
            ResizeCards();
            cards.ResumeLayout();
            UpdateStatus();
        }

        private void AddArticle(NewsArticle article, NewsSnapshot snapshot)
        {
            var source = ServerNews.Sources.First(item => item.Id == article.SourceId);
            var card = NewCard();
            var sourceLabel = new Label { Name = "articleSource", Text = source.Name.ToUpperInvariant(), UseMnemonic = false, ForeColor = SourceColor(source.Id), AutoEllipsis = true };
            var title = new LinkLabel { Text = article.Title, UseMnemonic = false, LinkBehavior = LinkBehavior.HoverUnderline, Font = titleFont, LinkColor = Theme.Foreground, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Foreground, Location = new Point(12, 36), Height = 53, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            title.LinkClicked += (sender, args) => OpenUrl(source, article.Url);
            string date = article.PublishedUtc.HasValue ? article.PublishedUtc.Value.ToString("d MMM yyyy", Culture) : AOLauncher.Localization.Texts.Get("Text_Date_not_supplied_FF3BEBBF83A3");
            var publication = new Label { Name = "articleDate", Text = date, ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopRight, AutoEllipsis = true };
            string summary = article.Summary;
            bool older = article.PublishedUtc.HasValue && article.PublishedUtc.Value < DateTime.UtcNow.AddMonths(-6);
            if (older && ReferenceEquals(article, snapshot.Articles.FirstOrDefault()))
                summary = snapshot.IsCached || snapshot.Unavailable
                    ? AOLauncher.Localization.Texts.Get("Text_Latest_saved_article_Check_the_official_6E700008C572")
                    : AOLauncher.Localization.Texts.Get("Text_Latest_published_here_This_source_has_AACDE1CE3EC5");
            if (string.IsNullOrWhiteSpace(summary)) summary = AOLauncher.Localization.Texts.Get("Text_Read_the_full_announcement_on_the_0733889992DF");
            if (snapshot.IsCached) summary = AOLauncher.Localization.Texts.Get("Text_Cached_E95A2EE0EC1E") + summary;
            var excerpt = new Label { Text = summary, UseMnemonic = false, ForeColor = Theme.Muted, Location = new Point(12, 114), Height = 49, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            card.Controls.Add(sourceLabel); card.Controls.Add(title); card.Controls.Add(publication); card.Controls.Add(excerpt);
            title.Name = "articleTitle"; title.AutoEllipsis = true;
            excerpt.Name = "articleExcerpt";
            card.Tag = "article:" + source.Id;
            cards.Controls.Add(card);
        }

        private void AddMessage(string title, string text, NewsSource source = null)
        {
            var card = NewCard();
            card.Height = source == null ? 137 : 161;
            var headingLabel = new Label { Text = title, Font = titleFont, ForeColor = Theme.Foreground, Location = new Point(12, 12), Height = 44 };
            var body = new Label { Text = text, ForeColor = Theme.Muted, Location = new Point(12, 60), Height = 70 };
            card.Controls.Add(headingLabel); card.Controls.Add(body);
            if (source != null)
            {
                var link = new LinkLabel { Text = AOLauncher.Localization.Texts.Get("Text_Open_website_67E22946F3C9"), LinkColor = Theme.Accent, Location = new Point(12, 136), Height = 20 };
                link.LinkClicked += (sender, args) => OpenUrl(source, source.SiteUrl);
                card.Controls.Add(link);
            }
            cards.Controls.Add(card);
        }

        private Panel NewCard()
        {
            var card = new NexusPanel { BackColor = Theme.SurfaceRaised, Margin = new Padding(0, 0, 0, 10), Width = Math.Max(100, cards.ClientSize.Width - 22) };
            return card;
        }

        private static Color SourceColor(string id) => id == "rebirth" ? Color.FromArgb(115, 197, 208) : Color.FromArgb(157, 177, 190);

        private void ResizeCards()
        {
            int width = Math.Max(100, cards.ClientSize.Width - 4);
            foreach (Control card in cards.Controls)
            {
                card.Width = width;
                if (card.Tag is string tag && tag.StartsWith("article:", StringComparison.Ordinal)) LayoutArticle(card);
                else foreach (Control child in card.Controls) child.Width = Math.Max(60, width - 24);
            }
        }

        private void LayoutArticle(Control card)
        {
            int inset = NexusChrome.Scale(this, 12), gap = NexusChrome.Scale(this, 6);
            int width = Math.Max(40, card.Width - inset * 2);
            var source = card.Controls["articleSource"];
            var date = card.Controls["articleDate"];
            var title = card.Controls["articleTitle"];
            var excerpt = card.Controls["articleExcerpt"];
            int headerHeight = Math.Max(source.Font.Height, date.Font.Height) + 2;
            int dateWidth = Math.Min(width / 2, TextRenderer.MeasureText(date.Text, date.Font).Width + 2);
            source.SetBounds(inset, inset, Math.Max(1, width - dateWidth - gap), headerHeight);
            date.SetBounds(card.Width - inset - dateWidth, inset, dateWidth, headerHeight);
            int titleHeight = Math.Min(title.Font.Height * 3, TextRenderer.MeasureText(title.Text, title.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
            title.SetBounds(inset, source.Bottom + gap, width, titleHeight);
            int excerptHeight = Math.Min(excerpt.Font.Height * 3, TextRenderer.MeasureText(excerpt.Text, excerpt.Font,
                new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height);
            excerpt.SetBounds(inset, title.Bottom + gap, width, excerptHeight);
            card.Height = excerpt.Bottom + inset;
        }

        private void UpdateStatus()
        {
            status.Visible=true;
            int selectedCount = ServerNews.Sources.Count(source => selectedSources.Contains(source.Id));
            if (selectedCount == 0)
            {
                status.Text = selectedSources.Contains("custom") ? AOLauncher.Localization.Texts.Get("Text_No_public_news_feed_configured_F26DF34A6468") : AOLauncher.Localization.Texts.Get("Text_No_news_sources_selected_8B02D1B163C0");
                return;
            }
            string sourceCount = selectedCount.ToString(Culture) + (selectedCount == 1 ? AOLauncher.Localization.Texts.Get("Text_source_F053A37709C2") : AOLauncher.Localization.Texts.Get("Text_sources_F09BD0006E5D"));
            if (offlinePreview) { status.Text = AOLauncher.Localization.Texts.Get("News.SamplePreview"); return; }
            if (refreshing) { status.Text = AOLauncher.Localization.Texts.Get("Text_Updating_7EF2A329326F") + sourceCount + "…"; return; }
            var visible = snapshots.Where(snapshot => selectedSources.Contains(snapshot.SourceId)).ToArray();
            int unavailable = visible.Count(snapshot => snapshot.Unavailable);
            var last = visible.Where(snapshot => snapshot.FetchedUtc != DateTime.MinValue).Select(snapshot => snapshot.FetchedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            if (unavailable > 0)
                status.Text = AOLauncher.Localization.Texts.Get("Text_Some_sources_offline_E8BC64AB7171") + (last == DateTime.MinValue ? AOLauncher.Localization.Texts.Get("Text_Try_Refresh_BFF8733D9F81") : AOLauncher.Localization.Texts.Get("Text_Last_saved_update_1068DD393F96") + last.ToLocalTime().ToString("d MMM · HH:mm", Culture));
            else if (last == DateTime.MinValue) status.Text = sourceCount + AOLauncher.Localization.Texts.Get("Text_Waiting_for_the_first_update_8740E2D34976");
            else { status.Visible=false;tips.SetToolTip(refresh,AOLauncher.Localization.Texts.Get("Text_Last_updated_0EFAD8B31642")+last.ToLocalTime().ToString("d MMM · HH:mm",Culture)); }
        }

        private void OpenSource()
        {
            var selected = ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)).ToArray();
            if (selected.Length > 1)
            {
                var menu = CreateSourceMenu();
                menu.Closed += (sender, args) => QueueMenuDisposal(menu);
                menu.Show(sourceLink, new Point(0, sourceLink.Height));
            }
            else if (selected.Length == 1) OpenUrl(selected[0], selected[0].SiteUrl);
        }

        private ContextMenuStrip CreateSourceMenu()
        {
            var menu = NewSourceMenu(false);
            foreach (var source in ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)))
            {
                var current = source;
                var item = NewMenuItem(source.Name);
                item.ForeColor = SourceColor(source.Id);
                item.Tag = source.Id;
                item.Click += (sender, args) => OpenUrl(current, current.SiteUrl);
                menu.Items.Add(item);
            }
            return menu;
        }

        private ContextMenuStrip NewSourceMenu(bool checkable)
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Theme.SurfaceRaised,
                ForeColor = Theme.Foreground,
                Font = Font,
                ShowImageMargin = false,
                ShowCheckMargin = checkable,
                Padding = new Padding(4),
                MinimumSize = new Size(210, 0),
                Renderer = new SourceMenuRenderer()
            };
            ownedMenus.Add(menu);
            menu.Disposed += (sender, args) => ownedMenus.Remove(menu);
            return menu;
        }

        private void QueueMenuDisposal(ContextMenuStrip menu)
        {
            // WinForms reparents a dropdown after raising Closed; disposing inside
            // that event would make Close access a destroyed handle. The panel owns
            // every popup so shutdown also releases any queued or unopened menus.
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!menu.IsDisposed) menu.Dispose(); })); }
            catch (InvalidOperationException) { }
        }

        private static ToolStripMenuItem NewMenuItem(string text)
        {
            return new ToolStripMenuItem(text) { Padding = new Padding(5, 5, 5, 5), ForeColor = Theme.Foreground, BackColor = Theme.SurfaceRaised };
        }

        // ContextMenuStrip otherwise mixes the dark window colors with the Windows
        // image gutter and selection gradient. Paint the popup using one palette.
        internal sealed class SourceMenuRenderer : ToolStripProfessionalRenderer
        {
            public SourceMenuRenderer() : base(new SourceMenuColors()) { RoundedEdges = false; }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
                using (var brush = new SolidBrush(Theme.SurfaceRaised)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle box = e.ImageRectangle;
                int size = Math.Max(8, Math.Min(box.Width, box.Height) - 4);
                int left = box.Left + (box.Width - size) / 2, top = box.Top + (box.Height - size) / 2;
                using (var pen = new Pen(e.Item.Selected || e.Item.Pressed ? Theme.Background : Theme.Accent, 2F))
                    e.Graphics.DrawLines(pen, new[] { new Point(left, top + size / 2), new Point(left + size / 3, top + size - 2), new Point(left + size, top + 1) });
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, 5, e.Item.Height / 2, e.Item.Width - 5, e.Item.Height / 2);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                using (var brush = new SolidBrush(e.Item.Selected || e.Item.Pressed ? Theme.Accent : Theme.SurfaceRaised))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected || e.Item.Pressed ? Theme.Background : e.Item.ForeColor;
                base.OnRenderItemText(e);
            }
        }

        private sealed class SourceMenuColors : ProfessionalColorTable
        {
            public SourceMenuColors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground => Theme.SurfaceRaised;
            public override Color ImageMarginGradientBegin => Theme.SurfaceRaised;
            public override Color ImageMarginGradientMiddle => Theme.SurfaceRaised;
            public override Color ImageMarginGradientEnd => Theme.SurfaceRaised;
            public override Color MenuBorder => Theme.Border;
            public override Color MenuItemSelected => Theme.Accent;
            public override Color MenuItemBorder => Theme.Accent;
            public override Color MenuItemSelectedGradientBegin => Theme.Accent;
            public override Color MenuItemSelectedGradientEnd => Theme.Accent;
        }

        private void OpenUrl(NewsSource source, string url)
        {
            string safe = ServerNews.SafeUrl(source, url);
            if (safe == null) return;
            try { Process.Start(new ProcessStartInfo(safe) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            {
                NexusMessageBox.Show(this, AOLauncher.Localization.Texts.Get("Text_Windows_could_not_open_your_browser_9F39318C9E63") + safe, "AO Nexus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                tips.Dispose();lifetime.Cancel(); automaticRefresh.Stop(); automaticRefresh.Dispose(); interactionRefresh.Stop(); interactionRefresh.Dispose();
                foreach (var menu in ownedMenus.ToArray()) menu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing && !resourcesDisposed) { lifetime.Dispose(); headingFont.Dispose(); titleFont.Dispose(); resourcesDisposed = true; }
        }

    }
}
