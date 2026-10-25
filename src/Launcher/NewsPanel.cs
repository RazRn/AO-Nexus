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
        private readonly Label subtitle = new Label();
        private readonly Label status = new Label();
        private readonly Button filter = new Button();
        private readonly Button refresh = new Button();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private readonly LinkLabel sourceLink = new LinkLabel();
        private readonly System.Windows.Forms.Timer automaticRefresh = new System.Windows.Forms.Timer();
        private readonly Font headingFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        private readonly Font titleFont = new Font("Segoe UI", 11F, FontStyle.Bold);
        private NewsSnapshot[] snapshots;
        private bool refreshing;
        private bool started;
        private bool resourcesDisposed;
        private bool applyingPreferences;
        private readonly HashSet<string> selectedSources = new HashSet<string>(ServerNews.Sources.Select(source => source.Id), StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ContextMenuStrip> ownedMenus = new HashSet<ContextMenuStrip>();
        private ContextMenuStrip selectionMenu;

        public event EventHandler SelectionPreferencesChanged;
        public string[] SelectedSourceIds => ServerNews.Sources.Select(source => source.Id).Concat(new[] { "custom" }).Where(selectedSources.Contains).ToArray();

        public NewsPanel(bool offlinePreview = false)
        {
            this.offlinePreview = offlinePreview;
            snapshots = offlinePreview ? ServerNews.PreviewSnapshots() : new NewsSnapshot[0];
            BackColor = Theme.Surface;
            ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(16, 14, 16, 10);
            Name = "serverNewsPanel";
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = BackColor };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            heading.Dock = DockStyle.Fill; heading.Font = headingFont; heading.ForeColor = Theme.Accent; heading.Margin = Padding.Empty;
            subtitle.Dock = DockStyle.Fill; subtitle.ForeColor = Theme.Muted; subtitle.Margin = Padding.Empty;
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 5) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            filter.Dock = DockStyle.Fill; filter.FlatStyle = FlatStyle.Flat; filter.FlatAppearance.BorderColor = Theme.Border;
            filter.BackColor = Theme.SurfaceRaised; filter.ForeColor = Theme.Foreground; filter.Margin = new Padding(0, 0, 7, 0); filter.Name = "newsSourceFilter";
            filter.TextAlign = ContentAlignment.MiddleLeft; filter.Padding = new Padding(8, 0, 5, 0); filter.AutoEllipsis = true;
            filter.Cursor = Cursors.Hand; filter.AccessibleRole = AccessibleRole.ButtonDropDown;
            filter.Click += (sender, args) => OpenSelectionMenu();
            filter.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Down) { args.Handled = true; args.SuppressKeyPress = true; OpenSelectionMenu(); } };
            refresh.Dock = DockStyle.Fill; refresh.FlatStyle = FlatStyle.Flat; refresh.FlatAppearance.BorderColor = Theme.Border;
            refresh.BackColor = Theme.SurfaceRaised; refresh.ForeColor = Theme.Accent; refresh.Margin = Padding.Empty; refresh.Cursor = Cursors.Hand; refresh.Name = "refreshNews";
            refresh.Click += async (sender, args) => await RefreshNewsAsync();
            toolbar.Controls.Add(filter, 0, 0); toolbar.Controls.Add(refresh, 1, 0);
            cards.Dock = DockStyle.Fill; cards.AutoScroll = true; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false;
            cards.Margin = Padding.Empty; cards.Padding = Padding.Empty; cards.BackColor = Theme.Surface; cards.Name = "newsCards";
            cards.ClientSizeChanged += (sender, args) => ResizeCards();
            status.Dock = DockStyle.Fill; status.ForeColor = Theme.Muted; status.TextAlign = ContentAlignment.MiddleLeft; status.Margin = new Padding(0, 3, 0, 0);
            sourceLink.Dock = DockStyle.Fill; sourceLink.LinkColor = Theme.Accent; sourceLink.ActiveLinkColor = Theme.Amber; sourceLink.VisitedLinkColor = Theme.Accent;
            sourceLink.Margin = Padding.Empty; sourceLink.TextAlign = ContentAlignment.MiddleLeft; sourceLink.LinkClicked += (sender, args) => OpenSource();
            layout.Controls.Add(heading, 0, 0); layout.Controls.Add(subtitle, 0, 1); layout.Controls.Add(toolbar, 0, 2);
            layout.Controls.Add(SlimScrolling.Wrap(cards), 0, 3); layout.Controls.Add(status, 0, 4); layout.Controls.Add(sourceLink, 0, 5);
            Controls.Add(layout);
            automaticRefresh.Interval = 15 * 60 * 1000;
            automaticRefresh.Tick += async (sender, args) => await RefreshNewsAsync();
            RefreshLanguage();
            Load += async (sender, args) =>
            {
                if (started || offlinePreview) return;
                started = true;
                // Read cache away from the UI thread, then replace it with independently fetched sources.
                try
                {
                    var cached = await Task.Run(() => ServerNews.Sources.Select(source => ServerNews.ReadCache(source, ServerNews.CacheDirectory)).ToArray(), lifetime.Token);
                    if (IsDisposed || lifetime.IsCancellationRequested) return;
                    snapshots = cached;
                    Render();
                    await RefreshNewsAsync();
                    if (!IsDisposed) automaticRefresh.Start();
                }
                catch (OperationCanceledException) { }
            };
        }

        private static string L(string english, string danish) => Texts.CurrentLanguage == "da" ? danish : english;
        private static CultureInfo Culture => CultureInfo.GetCultureInfo(Texts.CurrentLanguage == "da" ? "da-DK" : "en-GB");

        public void RefreshLanguage()
        {
            heading.Text = L("TRANSMISSIONS", "TRANSMISSIONER");
            subtitle.Text = L("News from the official server websites", "Nyheder fra servernes officielle hjemmesider");
            refresh.Text = L("Refresh", "Opdater");
            refresh.AccessibleName = L("Refresh server news", "Opdater servernyheder");
            filter.AccessibleName = L("Choose news sources", "Vælg nyhedskilder");
            if (selectionMenu != null) selectionMenu.Close();
            UpdateFilterCaption();
            Render();
        }

        public void ApplySelectionPreferences(IEnumerable<string> sourceIds)
        {
            applyingPreferences = true;
            try
            {
                selectedSources.Clear();
                selectedSources.UnionWith(ServerNews.NormalizeSourceIds(sourceIds));
                UpdateFilterCaption();
                Render();
            }
            finally { applyingPreferences = false; }
        }

        public void SetServerFilters(IEnumerable<string> sourceIds)
        {
            var values = ServerNews.NormalizeSourceIds(sourceIds);
            if (selectedSources.SetEquals(values)) return;
            selectedSources.Clear(); selectedSources.UnionWith(values);
            UpdateFilterCaption();
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

        private void UpdateFilterCaption()
        {
            var selected = ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)).ToArray();
            bool custom = selectedSources.Contains("custom");
            string text;
            if (selected.Length == 0) text = custom ? L("Local / custom server", "Lokal / tilpasset server") : L("No sources selected", "Ingen kilder valgt");
            else if (selected.Length == ServerNews.Sources.Length && !custom) text = L("All news sources", "Alle nyhedskilder");
            else text = string.Join(" + ", selected.Select(source => source.Id == "prk" ? "PRK" : source.Name)) + (custom ? L(" + custom", " + tilpasset") : "");
            filter.Text = text + "  ▾";
            filter.AccessibleDescription = string.Join(", ", selected.Select(source => source.Name).Concat(custom ? new[] { L("Local / custom server", "Lokal / tilpasset server") } : new string[0]));
            if (selectionMenu != null) UpdateMenuChecks(selectionMenu);
        }

        public async Task RefreshNewsAsync()
        {
            if (refreshing || IsDisposed || lifetime.IsCancellationRequested) return;
            if (offlinePreview || !ServerNews.Sources.Any(source => selectedSources.Contains(source.Id))) { Render(); return; }
            refreshing = true;
            refresh.Enabled = false;
            UpdateStatus();
            try
            {
                snapshots = await ServerNews.RefreshAsync(lifetime.Token);
                if (!IsDisposed && !lifetime.IsCancellationRequested) Render();
            }
            catch (OperationCanceledException) { }
            finally
            {
                refreshing = false;
                if (!IsDisposed && !lifetime.IsCancellationRequested) { refresh.Enabled = ServerNews.Sources.Any(source => selectedSources.Contains(source.Id)); UpdateStatus(); }
            }
        }

        private void Render()
        {
            if (IsDisposed) return;
            cards.SuspendLayout();
            foreach (Control child in cards.Controls.Cast<Control>().ToArray()) child.Dispose();
            cards.Controls.Clear();
            var selected = ServerNews.Sources.Where(source => selectedSources.Contains(source.Id)).ToArray();
            var visible = snapshots.Where(snapshot => selectedSources.Contains(snapshot.SourceId)).ToList();
            if (selectedSources.Count == 0)
            {
                AddMessage(L("No news sources selected", "Ingen nyhedskilder valgt"), L("Choose one or more sources from the menu above to show their news here. Your selection is saved automatically.", "Vælg en eller flere kilder i menuen ovenfor for at vise deres nyheder her. Dit valg gemmes automatisk."));
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
                        AddMessage(source.Name, L("News is temporarily unavailable. Use the official website or try Refresh.", "Nyheder er midlertidigt utilgængelige. Brug den officielle hjemmeside, eller tryk Opdater."), source);
                }
                if (cards.Controls.Count == 0) AddMessage(L("Connecting to Rubi-Ka", "Forbinder til Rubi-Ka"), L("Fetching the latest published articles. You can use the launcher while news loads.", "Henter de senest udgivne artikler. Du kan bruge launcheren, mens nyhederne hentes."));
            }
            if (selectedSources.Contains("custom"))
                AddMessage(L("Your own corner of Rubi-Ka", "Dit eget hjørne af Rubi-Ka"), L("Local and custom servers do not have a public news source configured. Choose one of the supported server feeds above.", "Lokale og tilpassede servere har ingen offentlig nyhedskilde konfigureret. Vælg en af servernes nyhedskilder ovenfor."));
            sourceLink.Visible = selected.Length > 0;
            sourceLink.Text = selected.Length > 1 ? L("Open server websites  ↗", "Åbn servernes hjemmesider  ↗") : L("Visit official news  ↗", "Besøg officielle nyheder  ↗");
            refresh.Enabled = !refreshing && selected.Length > 0;
            ResizeCards();
            cards.ResumeLayout();
            UpdateStatus();
        }

        private void AddArticle(NewsArticle article, NewsSnapshot snapshot)
        {
            var source = ServerNews.Sources.First(item => item.Id == article.SourceId);
            var card = NewCard();
            var sourceLabel = new Label { Text = source.Name.ToUpperInvariant(), UseMnemonic = false, ForeColor = SourceColor(source.Id), AutoEllipsis = true, Location = new Point(12, 12), Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var title = new LinkLabel { Text = article.Title, UseMnemonic = false, LinkBehavior = LinkBehavior.HoverUnderline, Font = titleFont, LinkColor = Theme.Foreground, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Foreground, Location = new Point(12, 36), Height = 53, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            title.LinkClicked += (sender, args) => OpenUrl(source, article.Url);
            string date = article.PublishedUtc.HasValue ? article.PublishedUtc.Value.ToString("d MMM yyyy", Culture) : L("Date not supplied", "Dato ikke oplyst");
            var publication = new Label { Text = date + (snapshot.IsCached ? L("  ·  cached", "  ·  gemt kopi") : ""), ForeColor = Theme.Muted, Location = new Point(12, 90), Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            string summary = article.Summary;
            bool older = article.PublishedUtc.HasValue && article.PublishedUtc.Value < DateTime.UtcNow.AddMonths(-6);
            if (older && ReferenceEquals(article, snapshot.Articles.FirstOrDefault()))
                summary = snapshot.IsCached || snapshot.Unavailable
                    ? L("Latest saved article. Check the official website for newer updates.", "Senest gemte artikel. Tjek den officielle hjemmeside for nyere opdateringer.")
                    : L("Latest published here. This source has not posted a newer article.", "Senest udgivet her. Denne kilde har ikke offentliggjort en nyere artikel.");
            if (string.IsNullOrWhiteSpace(summary)) summary = L("Read the full announcement on the official website.", "Læs hele meddelelsen på den officielle hjemmeside.");
            var excerpt = new Label { Text = summary, UseMnemonic = false, ForeColor = Theme.Muted, Location = new Point(12, 114), Height = 49, AutoEllipsis = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            card.Controls.Add(sourceLabel); card.Controls.Add(title); card.Controls.Add(publication); card.Controls.Add(excerpt);
            card.Height = 177;
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
                var link = new LinkLabel { Text = L("Open website  ↗", "Åbn hjemmeside  ↗"), LinkColor = Theme.Accent, Location = new Point(12, 136), Height = 20 };
                link.LinkClicked += (sender, args) => OpenUrl(source, source.SiteUrl);
                card.Controls.Add(link);
            }
            cards.Controls.Add(card);
        }

        private Panel NewCard()
        {
            var card = new Panel { BackColor = Theme.SurfaceRaised, Margin = new Padding(0, 0, 0, 10), Width = Math.Max(100, cards.ClientSize.Width - 22) };
            card.Paint += (sender, args) => { using (var pen = new Pen(Theme.Border)) args.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
            return card;
        }

        private static Color SourceColor(string id) => id == "rebirth" ? Theme.Accent : id == "prk" ? Theme.Amber : Color.FromArgb(153, 180, 223);

        private void ResizeCards()
        {
            int width = Math.Max(100, cards.ClientSize.Width - 4);
            foreach (Control card in cards.Controls)
            {
                card.Width = width;
                foreach (Control child in card.Controls) child.Width = Math.Max(60, width - 24);
            }
        }

        private void UpdateStatus()
        {
            int selectedCount = ServerNews.Sources.Count(source => selectedSources.Contains(source.Id));
            if (selectedCount == 0)
            {
                status.Text = selectedSources.Contains("custom") ? L("No public news feed configured", "Ingen offentlig nyhedskilde konfigureret") : L("No news sources selected", "Ingen nyhedskilder valgt");
                return;
            }
            string sourceCount = selectedCount.ToString(Culture) + (selectedCount == 1 ? L(" source", " kilde") : L(" sources", " kilder"));
            if (offlinePreview) { status.Text = L("Preview · ", "Forhåndsvisning · ") + sourceCount + L(" · verified headlines", " · bekræftede overskrifter"); return; }
            if (refreshing) { status.Text = L("Updating ", "Opdaterer ") + sourceCount + "…"; return; }
            var visible = snapshots.Where(snapshot => selectedSources.Contains(snapshot.SourceId)).ToArray();
            int unavailable = visible.Count(snapshot => snapshot.Unavailable);
            var last = visible.Where(snapshot => snapshot.FetchedUtc != DateTime.MinValue).Select(snapshot => snapshot.FetchedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
            if (unavailable > 0)
                status.Text = L("Some sources offline. ", "Nogle kilder er offline. ") + (last == DateTime.MinValue ? L("Try Refresh.", "Prøv Opdater.") : L("Last saved update: ", "Sidst gemte opdatering: ") + last.ToLocalTime().ToString("d MMM · HH:mm", Culture));
            else if (last == DateTime.MinValue) status.Text = sourceCount + L(" · Waiting for the first update", " · Venter på første opdatering");
            else status.Text = sourceCount + L(" · Updated ", " · Opdateret ") + last.ToLocalTime().ToString("d MMM · HH:mm", Culture) + L(" · every 15 min", " · hvert 15. min");
        }

        private void OpenSelectionMenu()
        {
            if (selectionMenu != null) { selectionMenu.Close(); return; }
            var menu = CreateSelectionMenu();
            selectionMenu = menu;
            menu.Closed += (sender, args) =>
            {
                if (ReferenceEquals(selectionMenu, menu)) selectionMenu = null;
                QueueMenuDisposal(menu);
            };
            menu.Show(filter, new Point(0, filter.Height));
        }

        private ContextMenuStrip CreateSelectionMenu()
        {
            var menu = NewSourceMenu(true);
            menu.Name = "newsSourceMenu";
            foreach (var source in ServerNews.Sources) AddSourceChoice(menu, source.Id, source.Name);
            AddSourceChoice(menu, "custom", L("Local / custom server", "Lokal / tilpasset server"));
            menu.Items.Add(new ToolStripSeparator());
            var all = NewMenuItem(L("Select all news sources", "Vælg alle nyhedskilder"));
            all.Name = "selectAllNewsSources";
            all.Click += (sender, args) => { SetServerFilters(null); UpdateMenuChecks(menu); };
            menu.Items.Add(all);
            var none = NewMenuItem(L("Clear selection", "Fravælg alle"));
            none.Name = "clearNewsSources";
            none.Click += (sender, args) => { SetServerFilters(new string[0]); UpdateMenuChecks(menu); };
            menu.Items.Add(none);
            // Check and convenience actions keep the popup open, so choosing several
            // feeds needs one interaction. Escape and clicks elsewhere still close it.
            menu.Closing += (sender, args) => { if (args.CloseReason == ToolStripDropDownCloseReason.ItemClicked) args.Cancel = true; };
            menu.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Space) return;
                var selected = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(item => item.Selected);
                if (selected == null) return;
                selected.PerformClick();
                args.Handled = true; args.SuppressKeyPress = true;
            };
            return menu;
        }

        private void AddSourceChoice(ContextMenuStrip menu, string id, string label)
        {
            var item = NewMenuItem(label);
            item.Name = "newsSource-" + id;
            item.Tag = id;
            item.Checked = selectedSources.Contains(id);
            item.Click += (sender, args) =>
            {
                var updated = new HashSet<string>(selectedSources, StringComparer.OrdinalIgnoreCase);
                if (!updated.Remove(id)) updated.Add(id);
                SetServerFilters(updated);
                UpdateMenuChecks(menu);
            };
            menu.Items.Add(item);
        }

        private void UpdateMenuChecks(ContextMenuStrip menu)
        {
            foreach (ToolStripMenuItem item in menu.Items.OfType<ToolStripMenuItem>())
                if (item.Tag is string id) item.Checked = selectedSources.Contains(id);
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
                MinimumSize = new Size(Math.Max(210, checkable ? filter.Width : 0), 0),
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
                e.TextColor = e.Item.Selected || e.Item.Pressed ? Theme.Background : Theme.Foreground;
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
                NexusMessageBox.Show(this, L("Windows could not open your browser. Visit: ", "Windows kunne ikke åbne din browser. Besøg: ") + safe, "AO Nexus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !resourcesDisposed)
            {
                lifetime.Cancel(); automaticRefresh.Stop(); automaticRefresh.Dispose();
                selectionMenu = null;
                foreach (var menu in ownedMenus.ToArray()) menu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing && !resourcesDisposed) { lifetime.Dispose(); headingFont.Dispose(); titleFont.Dispose(); resourcesDisposed = true; }
        }

    }
}
