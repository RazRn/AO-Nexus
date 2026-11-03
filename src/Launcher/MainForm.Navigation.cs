using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Section tabs under the header: Characters, Plugins and the Game GUI tools (Layouts,
    // Skins, Scripts, Backups). Plugins and Game GUI used to be separate windows behind
    // header buttons; they now open as pages in the main window. The log and the
    // "Log in selected" bar stay visible on every page.
    internal sealed partial class MainForm
    {
        Panel pageHost, charactersPage, pluginsPage, gameGuiPage;
        PluginsDialog pluginsView;
        GuiManagerDialog gameGuiView;
        CheckBox teamPluginsCheck, autoLoadCheck;
        bool refreshingTeamPlugins, refreshingAutoLoad, switchingSection;
        string currentSection = "characters";
        int pluginPageLoad;
        // Allows isolated UI tests to hold preparation without loading/injecting real plugins.
        internal Func<Task> BeforePluginsLoad;
        readonly Dictionary<string, NavTab> navTabs = new Dictionary<string, NavTab>();

        static readonly string[] GameGuiSections = { "layouts", "skins", "scripts", "backups" };

        // A flat tab with an accent underline, matching the dialogs' tab style.
        internal sealed class NavTab : Button
        {
            bool active;
            string badge;
            internal bool Active { get => active; set { active = value; Invalidate(); } }
            internal string Badge { get => badge; set { if (badge == value) return; badge = value; Invalidate(); } }
            internal NavTab(string text)
            {
                Text = text; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Theme.SurfaceRaised; FlatAppearance.MouseDownBackColor = Theme.Selection;
                BackColor = Color.Transparent; ForeColor = Theme.Foreground; Font = new Font("Segoe UI", 10.5F);
                AutoSize = false; Height = 46; Margin = Padding.Empty; Cursor = Cursors.Hand; TabStop = true; UseMnemonic = false;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            internal void FitWidth()
            {
                int text = TextRenderer.MeasureText(Text, Font).Width;
                int extra = string.IsNullOrEmpty(badge) ? 0 : TextRenderer.MeasureText(badge, new Font("Segoe UI", 8F, FontStyle.Bold)).Width + 14;
                Width = text + extra + 32;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var back = active ? Theme.SurfaceRaised : Parent?.BackColor ?? Theme.Background;
                if (!active && ClientRectangle.Contains(PointToClient(MousePosition)) && Enabled) back = Theme.SurfaceRaised;
                using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, ClientRectangle);
                var color = !Enabled ? Theme.Muted : active ? Theme.Accent : Color.FromArgb(199, 214, 221);
                int textWidth = TextRenderer.MeasureText(e.Graphics, Text, Font).Width;
                int badgeWidth = string.IsNullOrEmpty(badge) ? 0 : TextRenderer.MeasureText(e.Graphics, badge, new Font("Segoe UI", 8F, FontStyle.Bold)).Width + 10;
                int left = (Width - textWidth - (badgeWidth > 0 ? badgeWidth + 6 : 0)) / 2;
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, textWidth + 2, Height - 2), color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (badgeWidth > 0)
                {
                    var pill = new Rectangle(left + textWidth + 6, Height / 2 - 9, badgeWidth, 18);
                    using (var brush = new SolidBrush(Theme.Selection)) e.Graphics.FillRectangle(brush, pill);
                    TextRenderer.DrawText(e.Graphics, badge, new Font("Segoe UI", 8F, FontStyle.Bold), pill, Theme.Foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                if (active) using (var brush = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(brush, 0, Height - 3, Width, 3);
                if (Focused && ShowFocusCues) using (var pen = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 4);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        }

        void BuildNavigation(TableLayoutPanel layout)
        {
            var navigation = new TableLayoutPanel { Name="navigationBar", Dock=DockStyle.Fill, Margin=Padding.Empty,
                Padding=new Padding(0,0,0,1), RowCount=1, ColumnCount=2, BackColor=Theme.Surface };
            // Reserve the edge outside the child panels, so tabs cannot paint over
            // sections of it and leave disconnected border fragments between buttons.
            navigation.Paint += (sender, e) => {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 0, navigation.ClientSize.Height - 1, navigation.ClientSize.Width - 1, navigation.ClientSize.Height - 1);
            };
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            navigation.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var strip = new FlowLayoutPanel { Name = "sectionTabs", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(24, 0, 24, 0),
                BackColor = Theme.Surface, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
            AddNavTab(strip, "characters", AOLauncher.Localization.Texts.Get("Text_Characters_A2B5708F2F94"));
            AddNavTab(strip, "plugins", AOLauncher.Localization.Texts.Get("Text_Plugins_C62A56D7ECF3"));
            var teamWindowButton = new NavTab(AOLauncher.Localization.Texts.Get("Text_Control_Panel_27E27130F66F")) { Name="openTeamWindow" };
            teamWindowButton.FitWidth();
            teamWindowButton.Click += (sender,e) => ShowTeamWindow();
            var standalone = new FlowLayoutPanel { Name="controlPanelNavigation", Dock=DockStyle.Fill, AutoSize=true,
                AutoSizeMode=AutoSizeMode.GrowAndShrink, Margin=Padding.Empty, Padding=new Padding(12,0,24,0), BackColor=Theme.Surface, WrapContents=false };
            standalone.Controls.Add(new Panel { Width=1, Height=22, BackColor=Theme.Border, Margin=new Padding(0,12,12,0) });
            standalone.Controls.Add(teamWindowButton);
            var timersButton = new NavTab(AOLauncher.Localization.Texts.Get("Text_Timers_AF56D071FADE")) { Width=90 };
            timersButton.Click += (sender,e) => ShowTimers(); strip.Controls.Add(timersButton);
            // A thin divider separates the game GUI tools; no group label.
            strip.Controls.Add(new Panel { Width = 1, Height = 22, BackColor = Theme.Border, Margin = new Padding(12, 12, 12, 0) });
            AddNavTab(strip, "layouts", AOLauncher.Localization.Texts.Get("Text_Layouts_8D1D0CC42E06"));
            AddNavTab(strip, "skins", AOLauncher.Localization.Texts.Get("Text_Skins_A6432A47B0A5"));
            AddNavTab(strip, "scripts", AOLauncher.Localization.Texts.Get("Text_Scripts_Macros_F26188B354EF"));
            AddNavTab(strip, "backups", AOLauncher.Localization.Texts.Get("Text_Backups_DA61B377CA06"));
            strip.SizeChanged += (sender, e) => FitNavTabHeight(strip);
            standalone.SizeChanged += (sender, e) => FitNavTabHeight(standalone);
            foreach (string key in GameGuiSections) editingControls.Add(navTabs[key]);
            navTabs["characters"].Active = true;
            navigation.Controls.Add(strip,0,0);
            navigation.Controls.Add(standalone,1,0);
            layout.Controls.Add(navigation, 0, 1);
            FitNavTabHeight(strip);
            FitNavTabHeight(standalone);
            var nexusTree = (NexusTreeView)tree;
            nexusTree.RightText = node => node.Tag is CharacterConfig character
                ? PluginSets.Find(settings, character.PluginSetId)?.Name
                    ?? (string.IsNullOrEmpty(character.PluginSetId) ? AOLauncher.Localization.Texts.Get("Text_Automatic_270D51E07B76") : AOLauncher.Localization.Texts.Get("Text_No_plugins_2445FC9071A5")) : null;
            nexusTree.RightTextClicked += ShowPluginSetPicker;
        }

        static void FitNavTabHeight(FlowLayoutPanel strip)
        {
            int height = Math.Max(1, strip.ClientSize.Height - strip.Padding.Vertical);
            foreach (var tab in strip.Controls.OfType<NavTab>()) tab.Height = height;
        }

        void AddNavTab(FlowLayoutPanel strip, string key, string text)
        {
            var tab = new NavTab(text) { Name = "nav" + char.ToUpperInvariant(key[0]) + key.Substring(1), AccessibleName = text };
            tab.FitWidth();
            tab.Click += async (sender, e) => await ShowSection(key);
            strip.Controls.Add(tab);
            navTabs[key] = tab;
        }

        async Task ShowSection(string key)
        {
            if (switchingSection || key == currentSection) return;
            if (GameGuiSections.Contains(key) && (running || guiOperationBusy))
            { ShowInfo(AOLauncher.Localization.Texts.Get("Text_Wait_until_the_login_has_finished_8B6D5A8F8D67")); return; }
            if (!LeaveCurrentSection(key)) return;
            Task loading = null;
            switchingSection = true;
            try
            {
                if (key == "characters") charactersPage.BringToFront();
                else if (key != "plugins") ShowGameGuiPage(Array.IndexOf(GameGuiSections, key));
                currentSection = key;
                foreach (var pair in navTabs) pair.Value.Active = pair.Key == key;
                if (key == "plugins") loading = ShowPluginsPage();
            }
            finally { switchingSection = false; }
            // Navigation remains usable while preparation runs. A superseded load cannot
            // replace the current page or apply starter-set changes when it completes.
            if (loading != null) await loading;
        }

        // Unsaved plugin edits are offered for saving; a running Game GUI operation keeps its page.
        bool LeaveCurrentSection(string next)
        {
            if (currentSection == "plugins" && pluginsView != null && !pluginsView.IsDisposed && pluginsView.IsDirty)
            {
                var answer = NexusMessageBox.Show(this, AOLauncher.Localization.Texts.Get("Text_Save_your_plugin_changes_673207CBE0B7"), AOLauncher.Localization.Texts.Get("Text_Plugins_C62A56D7ECF3"),
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.Yes) ApplyPluginSettings(pluginsView.Working);
            }
            bool leavingGui = GameGuiSections.Contains(currentSection) && !GameGuiSections.Contains(next);
            if (leavingGui && gameGuiView != null && !gameGuiView.IsDisposed && (gameGuiView.IsBusy || guiOperationBusy))
            { ShowInfo(AOLauncher.Localization.Texts.Get("Text_Wait_for_the_current_game_GUI_B185B63727FC")); return false; }
            if (currentSection == "plugins") DisposePluginsPage();
            if (leavingGui) { DisposeGameGuiPage(); RefreshTree(tree.SelectedNode?.Tag); }
            return true;
        }

        async Task ShowPluginsPage()
        {
            DisposePluginsPage();
            int load = pluginPageLoad;
            if (pluginsPage == null) { pluginsPage = new Panel { Name = "pluginsPage", Dock = DockStyle.Fill, BackColor = Theme.Background }; pageHost.Controls.Add(pluginsPage); }
            var progress = new Label { Name = "pluginsLoading", Dock = DockStyle.Fill, ForeColor = Theme.Muted,
                Padding = new Padding(24), Text = AOLauncher.Localization.Texts.Get("Text_Preparing_plugins_You_can_use_the_AD484CBB54B0") };
            pluginsPage.Controls.Add(progress); progress.BringToFront(); pluginsPage.BringToFront();
            try
            {
                if (BeforePluginsLoad != null) await BeforePluginsLoad();
                AOSharpPack pack = null; string error = null;
                if (!smoke)
                {
                    var snapshot = new LauncherSettings { AOSharpPackPath = settings.AOSharpPackPath };
                    pack = await Task.Run(() => { try { AOSharpPack.MonitorPlugin(); } catch { } return pluginInjector.Pack(snapshot, out error); });
                }
                else error = AOLauncher.Localization.Texts.Get("Text_The_UI_preview_does_not_load_37ECD90FAF23");
                if (closing || IsDisposed || currentSection != "plugins" || load != pluginPageLoad) return;
                if (!smoke)
                {
                    if (pack != null && !settings.PluginSetsInitialized)
                    {
                        PluginSets.EnsureStarterSets(settings, pack);
                        SaveSettings();
                        AppendLog(string.Format(AOLauncher.Localization.Texts.Get("Text_Created_0_starter_plugin_sets_one_10908F883B78"), settings.PluginSets.Count));
                    }
                    else if (pack != null) MigrateSetsToZ(pack);
                }
                pluginsView = new PluginsDialog(settings, pack, error)
                {
                    RunningRows = () => pluginInjector.Rows,
                    LoadNow = (row, working) => pluginInjector.LoadNow(row, working),
                    UnloadNow = row => pluginInjector.UnloadNow(row),
                    SendPlugins = (row, paths, label) => pluginInjector.SendPlugins(row, settings, paths, label),
                    SetTesting = (row, testing) => pluginInjector.SetTesting(row, testing),
                    SaveTests = records =>
                    {
                        foreach (var record in records) settings.PluginTests.RemoveAll(item => string.Equals(item.PluginId, record.PluginId, StringComparison.OrdinalIgnoreCase));
                        settings.PluginTests.AddRange(records);
                        SaveSettings();
                        AppendLog(string.Format(AOLauncher.Localization.Texts.Get("Text_Plugin_test_0_plugins_tested_1_8CF1122B3879"), records.Count,
                            records.Count(record => record.Status != nameof(PluginStatus.Running) && record.Status != nameof(PluginStatus.Started))));
                    },
                    Log = AppendLog,
                    AutoInjectChanged = value => { SetAutoLoadPlugins(value); SyncPluginControls(); },
                    SaveAction = working => { ApplyPluginSettings(working); BeginInvoke((Action)(async () => await ReloadPluginsPage())); },
                    RevertAction = () => BeginInvoke((Action)(async () => await ReloadPluginsPage()))
                };
                pluginsView.UseEmbeddedButtons();
                pluginsView.EmbedIn(pluginsPage);
                pluginsPage.BringToFront();
            }
            finally { progress.Dispose(); }
        }

        async Task ReloadPluginsPage()
        {
            if (currentSection != "plugins" || closing || IsDisposed) return;
            await ShowPluginsPage();
        }

        void DisposePluginsPage()
        {
            pluginPageLoad++;
            if (pluginsView == null) return;
            var view = pluginsView; pluginsView = null;
            pluginsPage?.Controls.Remove(view);
            view.Dispose();
        }

        void ShowGameGuiPage(int tab)
        {
            if (gameGuiPage == null) { gameGuiPage = new Panel { Name = "gameGuiPage", Dock = DockStyle.Fill, BackColor = Theme.Background }; pageHost.Controls.Add(gameGuiPage); }
            if (gameGuiView == null || gameGuiView.IsDisposed)
            {
                var view = new GuiManagerDialog(settings, GuiLayouts.DefaultRoot, smoke) { DefaultTemplateId = settings.DefaultGuiTemplateId };
                view.SaveDefaultTemplate = id => { settings.DefaultGuiTemplateId = id; SaveSettings(); };
                view.SaveProfile = SaveSettings;
                view.RequestCancelOperation = () => guiCancellation?.Cancel();
                view.ExecuteOperation = (description, targets, installationWide, action) => ExecuteGuiOperation(view, description, targets, installationWide, action);
                view.PrepareEmbedded();
                view.EmbedIn(gameGuiPage);
                gameGuiView = view;
                // A page inside the main window may not raise Shown; load its lists explicitly.
                BeginInvoke((Action)(() => { if (!view.IsDisposed) view.RefreshLibrary(); }));
            }
            gameGuiView.ShowTab(tab);
            gameGuiPage.BringToFront();
        }

        void DisposeGameGuiPage()
        {
            if (gameGuiView == null) return;
            var view = gameGuiView; gameGuiView = null;
            gameGuiPage?.Controls.Remove(view);
            view.Dispose();
        }

        // The chip at the right of a character row: pick its default plugin set in place.
        void ShowPluginSetPicker(TreeNode node, Rectangle chip)
        {
            if (!(node.Tag is CharacterConfig character) || smoke) return;
            var menu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground, ShowCheckMargin = true, ShowImageMargin = false };
            void Add(string text, string id)
            {
                var item = new ToolStripMenuItem(text) { Checked = (character.PluginSetId ?? "") == (id ?? ""), ForeColor = Theme.Foreground };
                item.Click += (sender, e) =>
                {
                    character.PluginSetId = id;
                    SaveSettings(); tree.Invalidate();
                    AppendLog(character.Name + ": " + (id == null ? AOLauncher.Localization.Texts.Get("Text_automatic_plugin_set_picked_by_profession_5D27826B1AF4")
                        : id == PluginSet.None ? AOLauncher.Localization.Texts.Get("Text_no_plugins_C414317A576B") : AOLauncher.Localization.Texts.Get("Text_plugin_set_AB108BE9A439") + PluginSets.Find(settings, id)?.Name + "."));
                };
                menu.Items.Add(item);
            }
            Add(AOLauncher.Localization.Texts.Get("Text_Automatic_by_profession_48EB2631BDD8"), null);
            Add(AOLauncher.Localization.Texts.Get("Text_No_plugins_2445FC9071A5"), PluginSet.None);
            foreach (var set in settings.PluginSets.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) Add(set.Name, set.Id);
            menu.Items.Add(new ToolStripSeparator());
            var edit = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Edit_plugin_sets_8CEA545FF934")) { ForeColor = Theme.Accent };
            edit.Click += async (sender, e) => await ShowSection("plugins");
            menu.Items.Add(edit);
            menu.Closed += (sender, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(tree, new Point(chip.Left, chip.Bottom));
        }

        // Details panel: the character's set, its team override and whether plugins are loaded now.
        string PluginDetails(AccountConfig account, CharacterConfig character)
        {
            var set = PluginSets.Find(settings, character.PluginSetId);
            var lines = new List<string> { AOLauncher.Localization.Texts.Get("Text_Plugin_set_10CC3BA44F32") + (set != null ? set.Name + " · " + set.Plugins.Count + " plugins"
                : string.IsNullOrEmpty(character.PluginSetId) ? AOLauncher.Localization.Texts.Get("Text_automatic_picked_by_profession_the_first_0F3EFA98502D") : AOLauncher.Localization.Texts.Get("Text_none_A5529FB4ECA5")) };
            var preset = settings.Presets.FirstOrDefault(item => item.Id == settings.SelectedPresetId);
            var reference = preset?.Characters.FirstOrDefault(item => item.CharacterId == character.CharacterId && string.Equals(item.AccountId, account.Id, StringComparison.OrdinalIgnoreCase));
            if (reference != null)
                lines.Add(AOLauncher.Localization.Texts.Get("Text_In_this_team_FDF3B5000B22") + (!preset.LoadPlugins ? AOLauncher.Localization.Texts.Get("Text_no_plugins_switched_off_for_the_5206FE61FDFD")
                    : string.IsNullOrEmpty(reference.PluginSetId) ? AOLauncher.Localization.Texts.Get("Text_uses_the_character_default_8927DB9394EF")
                    : reference.PluginSetId == PluginSet.None ? AOLauncher.Localization.Texts.Get("Text_no_plugins_E69F128D8DFE") : PluginSets.Find(settings, reference.PluginSetId)?.Name ?? AOLauncher.Localization.Texts.Get("Text_none_A5529FB4ECA5")));
            var row = pluginInjector.Rows.FirstOrDefault(item => item.AccountId == account.Id && item.CharacterId == character.CharacterId);
            lines.Add(AOLauncher.Localization.Texts.Get("Text_Plugins_now_FE83243B4F8B") + (row == null ? AOLauncher.Localization.Texts.Get("Text_not_in_game_E505CB58E9D2") : row.StateText + (string.IsNullOrEmpty(row.Detail) ? "" : " · " + row.Detail)));
            return string.Join("\r\n", lines);
        }

        void UpdateTeamPluginsToggle(LaunchPreset preset)
        {
            if (teamPluginsCheck == null) return;
            refreshingTeamPlugins = true;
            try
            {
                teamPluginsCheck.Enabled = preset != null && !running;
                teamPluginsCheck.Checked = preset?.LoadPlugins ?? true;
            }
            finally { refreshingTeamPlugins = false; }
        }

        void SetAutoLoadPlugins(bool value)
        {
            if (refreshingAutoLoad || settings.AutoInjectPlugins == value) return;
            settings.AutoInjectPlugins = value;
            if (pluginsView != null && !pluginsView.IsDisposed) pluginsView.SetAutoInject(value);
            SaveSettings();
            AppendLog(value ? AOLauncher.Localization.Texts.Get("Text_Plugins_load_automatically_when_characters_enter_E506B48D2FBB")
                : AOLauncher.Localization.Texts.Get("Text_Automatic_plugin_loading_is_off_75E0A7895BEB"));
        }

        void SyncPluginControls()
        {
            // Plugins loaded by Nexus include the monitor, so show its measurements too.
            if (!smoke && settings.IncludeHealthMonitor && !settings.MonitorClientHealth
                && (settings.AutoInjectPlugins || pluginInjector.Rows.Any(row => row.State == "Loaded")))
            {
                settings.MonitorClientHealth = true;
                SaveSettings();
                AppendLog(AOLauncher.Localization.Texts.Get("Text_Responsiveness_monitoring_turned_on_Nexus_Health_F2F41E9DB5ED"));
            }
            if (autoLoadCheck != null && autoLoadCheck.Checked != settings.AutoInjectPlugins)
            {
                refreshingAutoLoad = true;
                try { autoLoadCheck.Checked = settings.AutoInjectPlugins; } finally { refreshingAutoLoad = false; }
            }
            if (navTabs.TryGetValue("plugins", out var tab))
            {
                int loaded = pluginInjector.Rows.Count(row => row.State == "Loaded");
                tab.Badge = loaded > 0 ? loaded.ToString() : null;
                tab.FitWidth();
            }
            // Polling changes live status, not the tree's plugin-set labels. Assignment
            // edits repaint the tree explicitly; invalidating here makes it flash every second.
            // Keep "Plugins now" in Details current when a loaded state changes.
            string stateKey = string.Join("|", pluginInjector.Rows.Select(row => row.AccountId + row.CharacterId + row.State));
            if (stateKey != lastPluginStateKey) { lastPluginStateKey = stateKey; if (tree?.SelectedNode?.Tag is CharacterConfig && !running) UpdateSelection(); }
        }
        string lastPluginStateKey;
    }
}
