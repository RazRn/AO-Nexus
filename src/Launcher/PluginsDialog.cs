using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Plugin sets, which set each character uses (by default and per team preset), the
    // AOSharp pack, automatic loading and the clients Nexus has loaded plugins into.
    // Edits apply to a copy and are kept on Save; Load/Unload act on running clients at once.
    internal sealed class PluginsDialog : EditorDialog
    {
        internal LauncherSettings Working { get; }
        readonly AOSharpPack pack;
        readonly string packError;
        readonly ListBox setList;
        readonly PluginGrid grid;
        readonly Label setTitle, setSuite, usedBy, pluginInfo;
        readonly TextBox search;
        readonly DataGridView assignments, running;
        readonly ComboBox assignmentScope, playStyle;
        readonly Label playAbout;
        readonly CheckBox autoInject, includeMonitor, includeShortcuts;
        readonly Label packStatus;
        readonly Button inGame, moveToZ;
        readonly Button copySetButton, renameSetButton, deleteSetButton;
        PluginSet allSet;
        readonly Dictionary<string, bool> folded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        // Commands each running client knows (Nexus Shortcuts LIST), refreshed in the background.
        readonly Dictionary<int, HashSet<string>> clientCommands = new Dictionary<int, HashSet<string>>();
        readonly Dictionary<int, DateTime> commandsAsked = new Dictionary<int, DateTime>();
        bool fillingStyle;
        // The pack and plugin list are read once and again only when the AOSharp folder or your DLLs
        // change; reading them on every click and keystroke made the page slow.
        AOSharpPack currentPack; string currentPackError;
        List<PluginInfo> catalogCache;
        string runningSignature;
        readonly Dictionary<string, Tuple<long, long, string>> testFileHashes = new Dictionary<string, Tuple<long, long, string>>(StringComparer.OrdinalIgnoreCase);
        PluginTestRecord CurrentTest(PluginInfo plugin)
        {
            var record = Working.PluginTests.LastOrDefault(item => string.Equals(item.PluginId, plugin.Id, StringComparison.OrdinalIgnoreCase));
            if (record?.TestKind != "Startup" || string.IsNullOrEmpty(plugin.Path)) return null;
            try {
                var file = new FileInfo(plugin.Path);
                if (!file.Exists) return null;
                Tuple<long, long, string> cached;
                if (!testFileHashes.TryGetValue(plugin.Path, out cached) || cached.Item1 != file.Length || cached.Item2 != file.LastWriteTimeUtc.Ticks) {
                    cached = Tuple.Create(file.Length, file.LastWriteTimeUtc.Ticks, PluginTestPolicy.FileHash(plugin.Path));
                    testFileHashes[plugin.Path] = cached;
                }
                return PluginTestPolicy.ResultMatches(record, cached.Item3) ? record : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        internal Func<List<PluginAutoInjector.Row>> RunningRows { private get; set; }
        internal Func<PluginAutoInjector.Row, LauncherSettings, string> LoadNow { private get; set; }
        internal Func<PluginAutoInjector.Row, string> UnloadNow { private get; set; }
        internal Action<string> Log { private get; set; }
        // Plugin test: load a list into one client, mark it as being tested, keep the results.
        internal Func<PluginAutoInjector.Row, string[], string, PluginHostClient.Response> SendPlugins { private get; set; }
        internal Action<PluginAutoInjector.Row, bool> SetTesting { private get; set; }
        internal Action<List<PluginTestRecord>> SaveTests { private get; set; }
        // Set when shown as the Plugins tab of the main window instead of a dialog.
        internal Action<LauncherSettings> SaveAction { private get; set; }
        internal Action RevertAction { private get; set; }
        // Embedded: the auto-load box applies at once, like the switch beside "Log in selected".
        internal Action<bool> AutoInjectChanged { private get; set; }
        string initialState;
        readonly CheckBox teamPlugins;
        string StateKey()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            return serializer.Serialize(new object[] { Working.SharedPlugins, Working.PluginSets, Working.CustomPlugins, Working.AOSharpPackPath, Working.AOSharpDisabled, Working.IncludeHealthMonitor, Working.IncludeShortcuts, Working.DefaultPlayStyle,
                Working.Accounts.SelectMany(a => a.Characters.Select(c => a.Id + c.CharacterId + "=" + c.PluginSetId)),
                Working.Presets.Select(p => p.Id + p.LoadPlugins + p.PlayStyle + string.Join(",", p.Characters.Select(c => c.CharacterId + "=" + c.PluginSetId))) });
        }
        internal bool IsDirty => StateKey() != initialState;
        // Keeps this page in step with the main window's auto-load switch.
        internal void SetAutoInject(bool value) { Working.AutoInjectPlugins = value; if (autoInject.Checked != value) autoInject.Checked = value; }
        internal void UseEmbeddedButtons()
        {
            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Save_changes_DB509B5D2B8F");
            CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Undo_changes_8395BC4C0866");
            CancelAction.DialogResult = DialogResult.None;
            CancelAction.Click += (sender, e) => RevertAction?.Invoke();
            AcceptButton = null; CancelButton = null;
        }

        internal PluginsDialog(LauncherSettings settings, AOSharpPack pack, string packError) : base(AOLauncher.Localization.Texts.Get("Text_Plugins_C62A56D7ECF3"), 640)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            Working = serializer.Deserialize<LauncherSettings>(serializer.Serialize(settings));
            Working.SharedPlugins = Working.SharedPlugins ?? new List<string>();
            this.pack = pack; this.packError = packError;
            ClientSize = new Size(1180, 720);
            FitContentOnOpen = false;
            DialogLayout.Controls.Remove(FieldsViewport);
            // Keep page edits next to the plugin toolbar, away from the global login action.
            var editActions = (FlowLayoutPanel)SaveButton.Parent;
            DialogLayout.Controls.Remove(editActions);
            DialogLayout.RowStyles[2] = new RowStyle(SizeType.Absolute, 0);
            DialogLayout.RowStyles[1] = new RowStyle(SizeType.AutoSize);
            ErrorLabel.AutoSize = true;
            ErrorLabel.Dock = DockStyle.Top;
            ErrorLabel.Margin = Padding.Empty;
            ErrorLabel.Visible = false;
            ErrorLabel.TextChanged += (sender, e) => {
                ErrorLabel.Visible = !string.IsNullOrWhiteSpace(ErrorLabel.Text);
                ErrorLabel.Margin = ErrorLabel.Visible ? new Padding(0, 6, 0, 0) : Padding.Empty;
            };
            DialogLayout.SizeChanged += (sender, e) => ErrorLabel.MaximumSize = new Size(Math.Max(100, DialogLayout.ClientSize.Width - DialogLayout.Padding.Horizontal), 0);
            var root = new TableLayoutPanel { Name = "pluginsPageLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            DialogLayout.Controls.Add(root, 0, 0);
            var toolTips = new ToolTip();
            Disposed += (sender, e) => toolTips.Dispose();

            // Top strip: automatic loading, Nexus's helpers, the AOSharp pack and bringing plugins in.
            var strip = new TableLayoutPanel { Name = "pluginToolbar", Dock = DockStyle.Top, AutoSize = false, AutoSizeMode = AutoSizeMode.GrowAndShrink, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(10, 6, 10, 6), BackColor = Theme.Surface };
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int index = 0; index < 2; index++) strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var toolbarHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Surface };
            toolbarHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbarHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbarHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editActions.Name = "pluginEditActions";
            editActions.BackColor = Theme.Surface;
            editActions.Dock = DockStyle.Top;
            SaveButton.Margin = new Padding(6, 0, 0, 0);
            CancelAction.Margin = new Padding(6, 0, 0, 0);
            var stripActions = new FlowLayoutPanel { Name = "componentActions", Dock = DockStyle.Top, AutoSize = false, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, BackColor = Theme.Surface };
            var stripRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = false, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, BackColor = Theme.Surface };
            autoInject = new NexusCheckBox { Name = "autoInjectPlugins", AutoSize = true, Checked = Working.AutoInjectPlugins, Margin = new Padding(0, 7, 18, 0),
                Text = AOLauncher.Localization.Texts.Get("Text_Auto_load_plugins_in_game_8A7161DA3BE2") };
            autoInject.CheckedChanged += (sender, e) => { Working.AutoInjectPlugins = autoInject.Checked; AutoInjectChanged?.Invoke(autoInject.Checked); };
            stripRow.Controls.Add(autoInject);
            includeShortcuts = new NexusCheckBox { Name = "includeShortcuts", AutoSize = true, Checked = Working.IncludeShortcuts, Margin = new Padding(0, 7, 18, 0),
                Text = AOLauncher.Localization.Texts.Get("Text_In_game_buttons_BDB9FAFB88C9") };
            includeShortcuts.CheckedChanged += (sender, e) => Working.IncludeShortcuts = includeShortcuts.Checked;
            stripRow.Controls.Add(includeShortcuts);
            includeMonitor = new NexusCheckBox { Name = "includeHealthMonitor", AutoSize = true, Checked = Working.IncludeHealthMonitor, Margin = new Padding(0, 7, 18, 0),
                Text = AOLauncher.Localization.Texts.Get("Text_Health_Monitor_658E8DFD2848") };
            includeMonitor.CheckedChanged += (sender, e) => Working.IncludeHealthMonitor = includeMonitor.Checked;
            stripRow.Controls.Add(includeMonitor);
            foreach (var check in new[] { autoInject, includeShortcuts, includeMonitor })
            {
                Action size = () => { check.AutoSize = false; check.Size = new Size(TextRenderer.MeasureText(check.Text, check.Font).Width + 30, Math.Max(26, check.Font.Height + 8)); };
                check.FontChanged += (sender, e) => size(); size();
            }
            packStatus = new Label { Name = "aosharpPackStatus", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 9, 14, 0), UseMnemonic = false };
            packStatus.MaximumSize = new Size(1000, 0);
            stripRow.Controls.Add(packStatus);
            stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Enable_AO_AE703235CC92"), (sender, e) => EnableAOSharp()));
            stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Disable_AO_B50DFF95494A"), (sender, e) => DisableAOSharp()));
            moveToZ = Action(AOLauncher.Localization.Texts.Get("Text_Move_sets_to_Z_suite_B5A2DA2AF091"), (sender, e) => MoveSetsToZ());
            moveToZ.Name = "moveSetsToZ";
            stripActions.Controls.Add(moveToZ);
            stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Import_from_AO_552A239128F0"), (sender, e) => Import()));
            stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Add_plugin_DLL_909A510CCB24"), (sender, e) => AddCustomPlugin()));
            stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_AO_folder_3A7EFA50C6BB"), (sender, e) => ChooseFolder()));
            if (AOSharpPack.HasEmbeddedPack)
                stripActions.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Use_built_in_EB07D362E6B4"), (sender, e) => { Working.AOSharpPackPath = null; ReloadPack(); ShowSelectedSet(); }));
            toolTips.SetToolTip(autoInject, AOLauncher.Localization.Texts.Get("Text_Nexus_waits_until_the_game_window_95ED5D20AA41"));
            toolTips.SetToolTip(includeShortcuts, AOLauncher.Localization.Texts.Get("Text_Adds_Nexus_Shortcuts_to_every_set_8C0B9BD15872"));
            toolTips.SetToolTip(includeMonitor, AOLauncher.Localization.Texts.Get("Text_Adds_Nexus_Health_Monitor_which_measures_7C0D96934A8D"));
            toolbarHeader.Controls.Add(stripRow, 0, 0);
            toolbarHeader.Controls.Add(editActions, 1, 0);
            strip.Controls.Add(toolbarHeader, 0, 0);
            strip.Controls.Add(stripActions, 0, 1);
            Action resizeToolbar = () =>
            {
                int width = Math.Max(100, strip.ClientSize.Width - strip.Padding.Horizontal);
                Func<FlowLayoutPanel, int, int> height = (flow, available) => {
                    int x = 0, total = 0, rowHeight = 0;
                    foreach (Control control in flow.Controls) {
                        if (!control.Visible) continue;
                        int w = control.Width + control.Margin.Horizontal, h = control.Height + control.Margin.Vertical;
                        if (x > 0 && x + w > available) { total += rowHeight; x = 0; rowHeight = 0; }
                        x += w; rowHeight = Math.Max(rowHeight, h);
                    }
                    return total + rowHeight;
                };
                int optionsWidth = Math.Max(100, width - editActions.GetPreferredSize(Size.Empty).Width);
                int optionsHeight = Math.Max(editActions.GetPreferredSize(Size.Empty).Height, height(stripRow, optionsWidth));
                int actionsHeight = Math.Max(34, height(stripActions, width));
                strip.RowStyles[0] = new RowStyle(SizeType.Absolute, optionsHeight);
                strip.RowStyles[1] = new RowStyle(SizeType.Absolute, actionsHeight);
                stripRow.Height = optionsHeight; stripActions.Height = actionsHeight;
                strip.Height = strip.Padding.Vertical + optionsHeight + actionsHeight;
            };
            strip.SizeChanged += (sender, e) => resizeToolbar();
            SaveButton.TextChanged += (sender, e) => resizeToolbar();
            CancelAction.TextChanged += (sender, e) => resizeToolbar();
            Shown += (sender, e) => resizeToolbar();
            root.Controls.Add(strip, 0, 0);

            var columns = new TableLayoutPanel { Name = "pluginColumns", Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(columns, 0, 1);

            // Column 1: plugin sets.
            var setsCard = Card(); columns.Controls.Add(setsCard, 0, 0);
            var left = Stack(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.AutoSize));
            left.Controls.Add(Heading(AOLauncher.Localization.Texts.Get("Text_PLUGIN_SETS_3E70E7AC5091")), 0, 0);
            setList = new ListBox { Name = "pluginSetList", Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Foreground,
                IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 30, Margin = Padding.Empty };
            setList.DrawItem += DrawSetItem;
            setList.SelectedIndexChanged += (sender, e) => ShowSelectedSet();
            left.Controls.Add(SlimScrolling.Wrap(setList), 0, 1);
            var setButtons = ActionGrid();
            setButtons.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_New_90661A0DD7D3"), (sender, e) => NewSet()));
            setButtons.Controls.Add(copySetButton = Action(AOLauncher.Localization.Texts.Get("Text_Copy_DA9FAA777787"), (sender, e) => CopySet()));
            setButtons.Controls.Add(renameSetButton = Action(AOLauncher.Localization.Texts.Get("Text_Rename_416E84460AC8"), (sender, e) => RenameSet()));
            setButtons.Controls.Add(deleteSetButton = Action(AOLauncher.Localization.Texts.Get("Text_Delete_50C904C3FE9F"), (sender, e) => DeleteSet()));
            left.Controls.Add(setButtons, 0, 2);
            setsCard.Controls.Add(left);

            // Column 2: the plugins in the selected set, grouped as in the mockup.
            var pluginsCard = Card(); columns.Controls.Add(pluginsCard, 1, 0);
            var middle = Stack(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.AutoSize));
            var titleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Surface };
            titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            setTitle = new Label { Name = "pluginSetTitle", AutoSize = true, Font = new Font("Segoe UI", 14F, FontStyle.Bold), ForeColor = Theme.Foreground, Margin = new Padding(0, 0, 10, 2), UseMnemonic = false };
            titleRow.Controls.Add(setTitle, 0, 0);
            setSuite = new Label { Name = "pluginSetSuite", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 9, 0, 0), UseMnemonic = false };
            titleRow.Controls.Add(setSuite, 1, 0);
            search = new NexusTextBox { Name = "pluginSearch", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            // Waits for a pause in typing instead of rebuilding the list on every key.
            var searchDelay = new Timer { Interval = 200 };
            searchDelay.Tick += (sender, e) => { searchDelay.Stop(); if (!IsDisposed) ShowSelectedSet(); };
            search.TextChanged += (sender, e) => { searchDelay.Stop(); searchDelay.Start(); };
            Disposed += (sender, e) => searchDelay.Dispose();
            toolTips.SetToolTip(search, AOLauncher.Localization.Texts.Get("Text_Search_plugins_823E71D628F6"));
            titleRow.Controls.Add(search, 2, 0);
            middle.Controls.Add(titleRow, 0, 0);
            usedBy = new Label { Name = "pluginSetUsedBy", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, 8), UseMnemonic = false };
            middle.Controls.Add(usedBy, 0, 1);
            grid = new PluginGrid { Name = "pluginGrid", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F), AccessibleName = AOLauncher.Localization.Texts.Get("Text_Plugins_in_this_set_AACF401F40D2") };
            grid.IsChecked = plugin => PluginSets.EffectiveIds(Working, SelectedSet).Contains(plugin.Id, StringComparer.OrdinalIgnoreCase);
            grid.IsInherited = plugin => SelectedSet != null && SelectedSet.Id != PluginSet.All && Working.SharedPlugins.Contains(plugin.Id, StringComparer.OrdinalIgnoreCase);
            grid.CheckChanged += PluginToggled;
            grid.TestOf = CurrentTest;
            grid.FocusChanged += plugin => pluginInfo.Text = PluginGrid.Describe(plugin, grid.TestOf(plugin)).Replace("\r\n", "  ·  ");
            middle.Controls.Add(grid, 0, 2);
            pluginInfo = new Label { Name = "pluginDetails", AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0), UseMnemonic = false,
                Text = AOLauncher.Localization.Texts.Get("Text_Point_at_a_plugin_to_see_A6D9C87D832F") };
            middle.Controls.Add(pluginInfo, 0, 3);
            pluginsCard.SizeChanged += (sender, e) =>
            {
                int width = Math.Max(100, pluginsCard.ClientSize.Width - 30);
                usedBy.MaximumSize = new Size(width, 0); pluginInfo.MaximumSize = new Size(width, 0);
            };
            pluginsCard.Controls.Add(middle);

            // Column 3: who uses which set and how the team plays; the clients running now.
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            columns.Controls.Add(right, 2, 0);
            var assignCard = Card(); assignCard.Margin = new Padding(0, 0, 0, 8); right.Controls.Add(assignCard, 0, 0);
            var assign = Stack(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize),
                new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100));
            assign.Controls.Add(Heading(AOLauncher.Localization.Texts.Get("Text_CHARACTERS_TEAMS_B4E610422D80")), 0, 0);
            assignmentScope = new NexusComboBox { Name = "pluginScope", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
            Theme.StyleCombo(assignmentScope);
            assignmentScope.Items.Add(AOLauncher.Localization.Texts.Get("Text_Characters_without_a_team_CBD01D76DCB5"));
            foreach (var preset in Working.Presets) assignmentScope.Items.Add(AOLauncher.Localization.Texts.Get("Text_Team_A14415E09B06") + preset.Name);
            assignmentScope.SelectedIndex = 0;
            assignmentScope.SelectedIndexChanged += (sender, e) => FillAssignments();
            assign.Controls.Add(assignmentScope, 0, 1);
            teamPlugins = new NexusCheckBox { Name = "teamLoadsPlugins", AutoSize = true, Visible = false, Margin = new Padding(0, 0, 0, 6),
                Text = AOLauncher.Localization.Texts.Get("Text_Load_plugins_when_this_team_logs_A431B4AEBB11") };
            teamPlugins.CheckedChanged += (sender, e) => { if (ScopePreset != null) ScopePreset.LoadPlugins = teamPlugins.Checked; playStyle.Enabled = ScopePreset == null || teamPlugins.Checked; };
            assign.Controls.Add(teamPlugins, 0, 2);
            var styleRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 4), BackColor = Theme.Surface };
            styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var howTo = new LinkLabel { Name = "howToPlay", Text = AOLauncher.Localization.Texts.Get("Text_Play_style_FA5181F8C3DF"), AutoSize = true,
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.Amber, Margin = new Padding(0, 6, 8, 0) };
            howTo.LinkClicked += (sender, e) => ShowHowTo(CurrentStyle);
            styleRow.Controls.Add(howTo, 0, 0);
            playStyle = new NexusComboBox { Name = "playStyle", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = Padding.Empty };
            Theme.StyleCombo(playStyle);
            foreach (var style in PlayStyles.All) playStyle.Items.Add(style);
            playStyle.SelectedIndexChanged += (sender, e) => PlayStyleChanged();
            styleRow.Controls.Add(playStyle, 1, 0);
            assign.Controls.Add(styleRow, 0, 3);
            // Keep help available without taking several rows away from the character list.
            playAbout = new Label { Name = "playStyleAbout", Visible = false, Margin = Padding.Empty };
            playAbout.TextChanged += (sender, e) => {
                toolTips.SetToolTip(playStyle, playAbout.Text);
                toolTips.SetToolTip(howTo, playAbout.Text);
                playStyle.AccessibleDescription = playAbout.Text;
            };
            assign.Controls.Add(playAbout, 0, 4);
            assignments = Grid("pluginAssignments");
            assignments.Columns.Add("Character", AOLauncher.Localization.Texts.Get("Text_Character_C8A1DC62F40D")); assignments.Columns[0].ReadOnly = true;
            assignments.Columns.Add(new DataGridViewComboBoxColumn { CellTemplate = new PluginSetComboCell(), Name = "Set", HeaderText = AOLauncher.Localization.Texts.Get("Text_Plugin_set_A8F029A60193"), FlatStyle = FlatStyle.Flat, FillWeight = 130 });
            assignments.CurrentCellDirtyStateChanged += (sender, e) => { if (assignments.IsCurrentCellDirty) assignments.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            assignments.CellValueChanged += AssignmentChanged;
            assignments.DataError += (sender, e) => e.ThrowException = false;
            assign.Controls.Add(SlimScrolling.Wrap(assignments), 0, 5);
            assignCard.Controls.Add(assign);

            var runningCard = Card(); runningCard.Margin = Padding.Empty; right.Controls.Add(runningCard, 0, 1);
            var runningStack = Stack(new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100), new RowStyle(SizeType.AutoSize));
            runningStack.Controls.Add(Heading(AOLauncher.Localization.Texts.Get("Text_RUNNING_NOW_7F978C431E64")), 0, 0);
            running = Grid("pluginClients");
            running.Columns.Add("Character", AOLauncher.Localization.Texts.Get("Text_Character_C8A1DC62F40D"));
            running.Columns.Add("Set", AOLauncher.Localization.Texts.Get("Text_Set_A72AF50D004F"));
            running.Columns.Add("State", AOLauncher.Localization.Texts.Get("Text_Plugins_C62A56D7ECF3"));
            foreach (DataGridViewColumn column in running.Columns) column.ReadOnly = true;
            running.ShowCellToolTips = true;
            runningStack.Controls.Add(SlimScrolling.Wrap(running), 0, 1);
            var runningButtons = ActionGrid();
            runningButtons.Name = "runningPluginActions";
            inGame = Action(AOLauncher.Localization.Texts.Get("Text_In_game_5A2963AE882D"), (sender, e) => ShowInGameMenu());
            inGame.Name = "inGameActions";
            toolTips.SetToolTip(inGame, AOLauncher.Localization.Texts.Get("Text_Run_team_commands_Form_team_Rebuff_53068BE9AB23"));
            runningButtons.Controls.Add(inGame);
            runningButtons.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Load_now_7DEC5737AAA3"), async (sender, e) => await ActOnRunning(true)));
            runningButtons.Controls.Add(Action(AOLauncher.Localization.Texts.Get("Text_Unload_02F38202250B"), async (sender, e) => await ActOnRunning(false)));
            var test = Action(AOLauncher.Localization.Texts.Get("Text_Test_plugins_A42814E6EACE"), (sender, e) => OpenTest());
            test.Name = "testPlugins";
            toolTips.SetToolTip(test, AOLauncher.Localization.Texts.Get("Text_Loads_plugins_into_one_character_a_7C1923EBD8EB"));
            runningButtons.Controls.Add(test);
            runningStack.Controls.Add(runningButtons, 0, 2);
            runningCard.Controls.Add(runningStack);
            var refresh = new Timer { Interval = 3000 };
            refresh.Tick += (sender, e) => { if (Visible && !running.IsCurrentCellInEditMode) FillRunning(); };
            Shown += (sender, e) => { FillRunning(); refresh.Start(); };
            VisibleChanged += (sender, e) => { if (Visible) { FillRunning(); refresh.Start(); } else refresh.Stop(); };
            Disposed += (sender, e) => refresh.Dispose();

            ReloadPack();
            FillSets();
            FillAssignments();
            SaveButton.Click += (sender, e) =>
            {
                if (Working.AOSharpDisabled && (RunningRows?.Invoke() ?? new List<PluginAutoInjector.Row>()).Any(row => row.State == "Loaded" || row.State == "Injecting")) { ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Wait_for_loading_to_finish_and_681397D7E0D0"); return; }
                Working.AutoInjectPlugins = !Working.AOSharpDisabled && autoInject.Checked;
                if (SaveAction != null) SaveAction(Working); else DialogResult = DialogResult.OK;
            };
            initialState = StateKey();
        }

        static Panel Card()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Theme.Surface, Margin = new Padding(0, 0, 8, 0) };
            panel.Paint += (sender, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1); };
            return panel;
        }
        static TableLayoutPanel Stack(params RowStyle[] rows)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty, BackColor = Theme.Surface };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (var row in rows) panel.RowStyles.Add(row); return panel;
        }
        static TableLayoutPanel ActionGrid()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 72,
                ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 6, 0, 0), BackColor = Theme.Surface };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            panel.ControlAdded += (sender, e) => {
                e.Control.AutoSize = false;
                e.Control.Dock = DockStyle.Fill;
                e.Control.Margin = new Padding(0, 2, panel.Controls.Count % 2 == 1 ? 6 : 0, 2);
                e.Control.MinimumSize = Size.Empty;
                e.Control.Padding = new Padding(4, 0, 4, 0);
            };
            return panel;
        }
        static Label Heading(string text) => new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8), UseMnemonic = false };

        void DrawSetItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= setList.Items.Count) return;
            var set = (PluginSet)setList.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = Rectangle.Inflate(e.Bounds, -8, 0);
            string suite = PluginSets.SuiteOf(set);
            string count = (PluginSets.MixesSuites(set) ? "⚠ " : suite == "AOSP" ? "AOSP · " : "") + PluginSets.EffectiveIds(Working, set).Count;
            int countWidth = TextRenderer.MeasureText(e.Graphics, count, setList.Font).Width;
            TextRenderer.DrawText(e.Graphics, set.Name, setList.Font, new Rectangle(bounds.X, bounds.Y, bounds.Width - countWidth - 6, bounds.Height), Theme.Foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, count, setList.Font, bounds, PluginSets.MixesSuites(set) ? Theme.Amber : Theme.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        static Button Action(string text, EventHandler click)
        {
            var button = Theme.Button(text); button.AutoSize = true; button.Font = new Font("Segoe UI", 9F);
            button.Margin = new Padding(0, 0, 6, 0); button.Click += click; return button;
        }
        static DataGridView Grid(string name) => new DataGridView { Name = name, Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Theme.Surface,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, EditMode = DataGridViewEditMode.EditOnEnter };

        void ReloadPack()
        {
            currentPack = pack; currentPackError = packError;
            if (!string.IsNullOrWhiteSpace(Working.AOSharpPackPath)) currentPack = AOSharpPack.FromFolder(Working.AOSharpPackPath, out currentPackError);
            catalogCache = null;
            ShowPackStatus();
        }

        void EnableAOSharp()
        {
            try
            {
                // AOSharp distribution is unapproved: activation uses the user's own folder.
                var candidate = new LauncherSettings { AOSharpPackPath = Working.AOSharpPackPath };
                var available = AOSharpPack.Locate(candidate, out var error);
                if (available == null)
                {
                    ChooseFolder();
                    if (string.IsNullOrWhiteSpace(Working.AOSharpPackPath)) return;
                    available = AOSharpPack.FromFolder(Working.AOSharpPackPath, out error);
                    if (available == null) throw new IOException(error);
                }
                Working.AOSharpDisabled = false;
                currentPack = available; currentPackError = null; catalogCache = null;
                PluginSets.EnsureStarterSets(Working, available);
                FillSets(); FillAssignments(); ShowSelectedSet();
                ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_AO_is_ready_Save_changes_to_6C9FC9A1F73F");
            }
            catch (Exception ex) { ErrorLabel.Text = ex.Message; }
        }

        void DisableAOSharp()
        {
            if ((RunningRows?.Invoke() ?? new List<PluginAutoInjector.Row>()).Any(row => row.State == "Loaded" || row.State == "Injecting"))
            {
                ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Unload_plugins_from_Running_now_or_9D3E85FF449C");
                return;
            }
            Working.AOSharpDisabled = true; Working.AutoInjectPlugins = false; autoInject.Checked = false;
            ShowPackStatus();
            ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Save_changes_to_disable_AO_Your_7C436156BD6F");
        }

        void ShowPackStatus()
        {
            var current = currentPack;
            int z = current?.Plugins.Count(plugin => plugin.Suite == "Z") ?? 0;
            string text = Working.AOSharpDisabled ? AOLauncher.Localization.Texts.Get("Text_AO_disabled_3B118E0249FC") : current != null
                ? "AO#: " + (current.Embedded ? AOLauncher.Localization.Texts.Get("Text_built_in_F0FE7471C06B") : AOLauncher.Localization.Texts.Get("Text_ready_973CD3B8651C")) + current.Plugins.Count + " plugins" + (z > 0 ? AOLauncher.Localization.Texts.Get("Text_main_suite_Z_9BF08FCBA462") : "")
                : AOLauncher.Localization.Texts.Get("Text_AO_not_available_99735B8AB93A") + currentPackError;
            if (packStatus.Text != text) packStatus.Text = text;
            packStatus.ForeColor = current != null ? Theme.Muted : Theme.Warning;
            bool showMove = z > 0 && Working.PluginSets.Any(set => PluginSets.SuiteOf(set) == "AOSP");
            if (moveToZ.Visible != showMove) moveToZ.Visible = showMove;
        }

        // A folder that stopped being a pack still shows the built-in plugins, as before.
        List<PluginInfo> Catalog()
        {
            if (catalogCache != null) return catalogCache;
            catalogCache = AOSharpPack.AllPlugins(Working, currentPack ?? pack);
            foreach (var plugin in catalogCache) { var caution = PluginCuration.Caution(plugin); if (caution != null && !(plugin.Note ?? "").Contains(caution)) plugin.Note = (plugin.Note + " " + caution).Trim(); }
            return catalogCache;
        }

        PluginSet SelectedSet => setList.SelectedItem as PluginSet;

        void FillSets(PluginSet select = null)
        {
            allSet = allSet ?? new PluginSet { Id = PluginSet.All, Name = "ALL" };
            allSet.Plugins = Working.SharedPlugins;
            setList.BeginUpdate(); setList.Items.Clear();
            setList.Items.Add(allSet);
            foreach (var set in Working.PluginSets.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) setList.Items.Add(set);
            setList.EndUpdate();
            if (select != null) setList.SelectedItem = select; else if (setList.Items.Count > 0) setList.SelectedIndex = 0; else ShowSelectedSet();
        }

        static readonly string[] GroupOrder = { "Profession handlers", "Managers", "Bots", "Automatons", "Utilities" };

        static string GroupTitle(string group)
        {
            switch (group)
            {
                case "Profession handlers": return AOLauncher.Localization.Texts.Get("Text_PROFESSION_HANDLERS_D13BB21B31AF");
                case "Managers": return AOLauncher.Localization.Texts.Get("Text_MANAGERS_1F5EC6EB8751");
                case "Bots": return AOLauncher.Localization.Texts.Get("Text_BOTS_BUDDIES_D169581E498B");
                case "Automatons": return AOLauncher.Localization.Texts.Get("Text_AUTOMATONS_DC9576CD3B13");
                case "Utilities": return AOLauncher.Localization.Texts.Get("Text_UTILITIES_803E67867F42");
                default: return (group ?? "").ToUpperInvariant();
            }
        }

        // Sections: the set's own suite first, then standalone plugins, Nexus's helpers and your DLLs;
        // the other suite last and folded, since it cannot run in the same set.
        void ShowSelectedSet()
        {
            var set = SelectedSet;
            grid.Enabled = set != null;
            copySetButton.Enabled = renameSetButton.Enabled = deleteSetButton.Enabled = set != null && set.Id != PluginSet.All;
            setTitle.Text = set == null ? AOLauncher.Localization.Texts.Get("Text_No_plugin_set_selected_4214D8B17B47") : set.Name;
            string filter = search.Text.Trim();
            var catalog = Catalog();
            var available = new HashSet<string>(catalog.Where(item => File.Exists(item.Path)).Select(item => item.Id), StringComparer.OrdinalIgnoreCase);
            var effective = new PluginSet { Plugins = PluginSets.EffectiveIds(Working, set).Where(available.Contains).ToList() };
            string suite = PluginSets.SuiteOf(new PluginSet { Plugins = (set?.Plugins ?? new List<string>()).Where(available.Contains).ToList() })
                ?? PluginSets.SuiteOf(effective) ?? PluginSets.SuiteOf(set) ?? (catalog.Any(item => item.Suite == "Z") ? "Z" : "AOSP");
            string other = suite == "Z" ? "AOSP" : "Z";
            setSuite.Text = set == null ? "" : (PluginSets.MixesSuites(effective)
                ? AOLauncher.Localization.Texts.Get("Text_mixed_suites_incompatible_plugins_will_be_4878A81EBBF7")
                : string.Format(AOLauncher.Localization.Texts.Get("Text_0_suite_5B9AEF66E4A0"), suite));
            setSuite.ForeColor = set != null && PluginSets.MixesSuites(effective) ? Theme.Amber : Theme.Muted;
            usedBy.Text = set == null ? AOLauncher.Localization.Texts.Get("Text_Create_a_set_with_New_on_C9DFCB47E8AB") : set.Id == PluginSet.All
                ? AOLauncher.Localization.Texts.Get("Text_Shared_by_every_enabled_plugin_set_F06C8BCCB8C7")
                : UsedByText(set) + (Working.SharedPlugins.Count > 0 ? AOLauncher.Localization.Texts.Get("Text_Includes_ALL_shared_plugins_FEDD8C33CC62") : "");
            Func<PluginInfo, bool> matches = item => filter.Length == 0 || item.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || item.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || (item.About ?? "").IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || item.Commands.Any(command => command.IndexOf(filter.TrimStart('/'), StringComparison.OrdinalIgnoreCase) >= 0);
            var sections = new List<PluginGrid.Section>();
            Action<string, string, string, IEnumerable<PluginInfo>, bool> add = (key, title, hint, plugins, foldByDefault) =>
            {
                var list = plugins.Where(matches).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (list.Count == 0) return;
                bool anyChecked = set != null && list.Any(item => grid.IsChecked(item));
                bool collapsed = folded.TryGetValue(key, out var state) ? state : foldByDefault && !anyChecked;
                if (filter.Length > 0) collapsed = false;
                sections.Add(new PluginGrid.Section { Key = key, Title = title, Hint = hint, Collapsed = collapsed, Plugins = list });
            };
            add("Recommended", AOLauncher.Localization.Texts.Get("Text_RECOMMENDED_STARTING_SET_1C1DDB7D0C61"), AOLauncher.Localization.Texts.Get("Text_chosen_defaults_gameplay_testing_still_required_11623E6070D5"), catalog.Where(item => PluginCuration.Tier(item, catalog) == "Recommended"), false);
            add("Specialist", AOLauncher.Localization.Texts.Get("Text_SPECIALIST_OPTIONAL_01CEDF5E3D9A"), null, catalog.Where(item => PluginCuration.Tier(item, catalog) == "Specialist"), true);
            add("Nexus", "AO NEXUS", AOLauncher.Localization.Texts.Get("Text_added_by_the_switches_above_AB02D257E4C0"), catalog.Where(item => item.Suite == "Nexus"), true);
            add("Custom", AOLauncher.Localization.Texts.Get("Text_YOUR_PLUGINS_E4E97E6A4665"), null, catalog.Where(item => item.Custom), false);
            add("Alternatives", AOLauncher.Localization.Texts.Get("Text_ALTERNATIVES_87D5F4CB5D4C"), AOLauncher.Localization.Texts.Get("Text_existing_choices_are_kept_E580D289C37C"), catalog.Where(item => PluginCuration.Tier(item, catalog) == "Alternatives"), true);
            if (set != null)
            {
                var missing = PluginSets.EffectiveIds(Working, set).Where(id => !catalog.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
                    .Select(id => new PluginInfo { Id = id, Name = id.Replace("pack:", ""), Note = AOLauncher.Localization.Texts.Get("Text_Not_in_the_AO_pack_or_2428556A449B") }).ToList();
                if (missing.Count > 0) sections.Insert(0, new PluginGrid.Section { Key = "Missing", Title = AOLauncher.Localization.Texts.Get("Text_MISSING_9A394A493906"), Plugins = missing });
            }
            grid.SetSections(sections);
            foreach (var section in sections) folded[section.Key] = section.Collapsed;
            ShowPackStatus();
        }

        void PluginToggled(PluginInfo plugin, bool on)
        {
            var set = SelectedSet;
            if (set == null || grid.IsInherited(plugin)) return;
            // Z and AOSP handlers/managers fight each other, so a set stays within one suite.
            string current = PluginSets.SuiteOf(set);
            if (on && (plugin.Suite == "Z" || plugin.Suite == "AOSP") && current != null && current != plugin.Suite)
            {
                var answer = NexusMessageBox.Show(this, string.Format(AOLauncher.Localization.Texts.Get("Text_0_uses_the_1_suite_and_09589722A5AB"), set.Name, current, plugin.Name, plugin.Suite),
                    AOLauncher.Localization.Texts.Get("Text_Plugin_suites_B7A37FF22B16"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { grid.Invalidate(); return; }
                PluginSets.Convert(set, plugin.Suite == "Z");
            }
            var ids = set.Plugins.Where(id => !string.Equals(id, plugin.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on) ids.Add(plugin.Id);
            set.Plugins = ids;
            if (set.Id == PluginSet.All)
            {
                Working.SharedPlugins = ids;
                PluginSets.SetSharedPlugin(Working, plugin.Id, on);
                set.Plugins = Working.SharedPlugins;
            }
            if (on && !string.IsNullOrEmpty(plugin.Note)) { ErrorLabel.ForeColor = Theme.Amber; ErrorLabel.Text = plugin.Name + ": " + plugin.Note; }
            setList.Invalidate();
            ShowSelectedSet();
        }

        void MoveSetsToZ()
        {
            var sets = Working.PluginSets.Where(set => PluginSets.SuiteOf(set) == "AOSP").ToList();
            if (sets.Count == 0) return;
            if (NexusMessageBox.Show(this, string.Format(AOLauncher.Localization.Texts.Get("Text_Move_0_plugin_sets_to_the_522D64C99EA7"), sets.Count),
                AOLauncher.Localization.Texts.Get("Text_Move_sets_to_Z_suite_9462E6FDD620"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var removed = new List<string>();
            foreach (var set in sets) removed.AddRange(PluginSets.Convert(set, true).Select(id => set.Name + ": " + id.Replace("pack:", "")));
            ErrorLabel.ForeColor = removed.Count == 0 ? Theme.Accent : Theme.Amber;
            ErrorLabel.Text = string.Format(AOLauncher.Localization.Texts.Get("Text_0_sets_moved_to_the_Z_33A8B78D7B8C"), sets.Count)
                + (removed.Count > 0 ? " " + AOLauncher.Localization.Texts.Get("Text_Removed_no_Z_counterpart_3689B2E11B75") + string.Join(", ", removed.Take(8)) : "");
            FillSets(SelectedSet);
        }

        string AskName(string title, string value)
        {
            using (var dialog = new TextPromptDialog(title, AOLauncher.Localization.Texts.Get("Text_Name_F4C5C27BA10A"), value))
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Value : null;
        }

        void NewSet()
        {
            string name = AskName(AOLauncher.Localization.Texts.Get("Text_New_plugin_set_8F31607A8BC3"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            var set = new PluginSet { Name = UniqueName(name.Trim()) };
            Working.PluginSets.Add(set); FillSets(set); FillAssignments();
        }
        void RenameSet()
        {
            if (SelectedSet == null || SelectedSet.Id == PluginSet.All) return;
            string name = AskName(AOLauncher.Localization.Texts.Get("Text_Rename_plugin_set_56622E1CB0D5"), SelectedSet.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            var set = SelectedSet; set.Name = UniqueName(name.Trim(), set); FillSets(set); FillAssignments();
        }
        void CopySet()
        {
            if (SelectedSet == null || SelectedSet.Id == PluginSet.All) return;
            var copy = new PluginSet { Name = UniqueName(SelectedSet.Name + AOLauncher.Localization.Texts.Get("Text_copy_EDC0DAEA2E72")), Plugins = SelectedSet.Plugins.ToList() };
            Working.PluginSets.Add(copy); FillSets(copy); FillAssignments();
        }
        void DeleteSet()
        {
            if (SelectedSet == null || SelectedSet.Id == PluginSet.All) return;
            if (NexusMessageBox.Show(this, string.Format(AOLauncher.Localization.Texts.Get("Text_Delete_the_plugin_set_0_Characters_FE228DE38B93"), SelectedSet.Name),
                AOLauncher.Localization.Texts.Get("Text_Delete_plugin_set_77F478F6EFD4"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            PluginSets.Delete(Working, SelectedSet); FillSets(); FillAssignments();
        }
        string UniqueName(string name, PluginSet except = null)
        {
            if (name.Length > 60) name = name.Substring(0, 60);
            string candidate = name; int suffix = 2;
            while (string.Equals(candidate, "ALL", StringComparison.OrdinalIgnoreCase) || Working.PluginSets.Any(set => set != except && string.Equals(set.Name, candidate, StringComparison.CurrentCultureIgnoreCase))) candidate = name + " " + suffix++;
            return candidate;
        }

        LaunchPreset ScopePreset => assignmentScope.SelectedIndex > 0 ? Working.Presets[assignmentScope.SelectedIndex - 1] : null;
        sealed class Choice { internal string Id, Text; public override string ToString() => Text; }
        const string DefaultChoice = "@default", AutomaticChoice = "@automatic";

        // The main window gave an automatic character its profession's set while this page was open:
        // keep the page's copy in step, without marking the page as changed when it was not.
        internal void AdoptAutomaticSet(string accountId, int characterId, PluginSet set)
        {
            bool clean = !IsDirty;
            if (!Working.PluginSets.Any(item => item.Id == set.Id)) Working.PluginSets.Add(new PluginSet { Id = set.Id, Name = set.Name, Plugins = set.Plugins.ToList() });
            var character = Working.Accounts.FirstOrDefault(item => item.Id == accountId)?.Characters.FirstOrDefault(item => item.CharacterId == characterId);
            if (character != null && string.IsNullOrEmpty(character.PluginSetId)) character.PluginSetId = set.Id;
            if (clean) initialState = StateKey();
            FillSets(SelectedSet); FillAssignments();
        }

        PlayStyle CurrentStyle => playStyle.SelectedItem as PlayStyle ?? PlayStyles.Find(null);

        void PlayStyleChanged()
        {
            var style = CurrentStyle;
            playAbout.Text = style.About + (style.Z.Length > 0 ? "  " + AOLauncher.Localization.Texts.Get("Text_Adds_5434EDD6B914") + string.Join(", ", style.Z.Select(id => id.Replace("Z.", ""))) : "");
            if (fillingStyle) return;
            string value = style.Id == PlayStyles.None ? null : style.Id;
            if (ScopePreset != null) ScopePreset.PlayStyle = value; else Working.DefaultPlayStyle = value;
        }

        void FillAssignments()
        {
            assignments.CellValueChanged -= AssignmentChanged;
            assignments.Rows.Clear();
            var preset = ScopePreset;
            if (teamPlugins != null) { teamPlugins.Visible = preset != null; if (preset != null) teamPlugins.Checked = preset.LoadPlugins; }
            fillingStyle = true;
            string styleId = preset != null ? preset.PlayStyle : Working.DefaultPlayStyle;
            playStyle.SelectedItem = playStyle.Items.Cast<PlayStyle>().FirstOrDefault(style => style.Id == PlayStyles.Find(styleId).Id);
            playStyle.Enabled = preset == null || preset.LoadPlugins;
            fillingStyle = false;
            PlayStyleChanged();
            var choices = new List<Choice>();
            if (preset != null) choices.Add(new Choice { Id = DefaultChoice, Text = AOLauncher.Localization.Texts.Get("Text_Character_default_201B9FB1B9D7") });
            else choices.Add(new Choice { Id = AutomaticChoice, Text = AOLauncher.Localization.Texts.Get("Text_Automatic_by_profession_48EB2631BDD8") });
            choices.Add(new Choice { Id = PluginSet.None, Text = AOLauncher.Localization.Texts.Get("Text_No_plugins_2445FC9071A5") });
            choices.AddRange(Working.PluginSets.OrderBy(set => set.Name, StringComparer.CurrentCultureIgnoreCase).Select(set => new Choice { Id = set.Id, Text = set.Name }));
            var column = (DataGridViewComboBoxColumn)assignments.Columns["Set"];
            column.Items.Clear(); foreach (var choice in choices) column.Items.Add(choice.Text);
            foreach (var account in Working.Accounts)
                foreach (var character in account.Characters)
                {
                    string id;
                    if (preset != null)
                    {
                        var reference = preset.Characters.FirstOrDefault(item => item.CharacterId == character.CharacterId && string.Equals(item.AccountId, account.Id, StringComparison.OrdinalIgnoreCase));
                        if (reference == null) continue;
                        id = string.IsNullOrEmpty(reference.PluginSetId) ? DefaultChoice : reference.PluginSetId;
                    }
                    else id = string.IsNullOrEmpty(character.PluginSetId) ? AutomaticChoice : character.PluginSetId;
                    var selected = choices.FirstOrDefault(choice => choice.Id == id) ?? choices.First(choice => choice.Id == PluginSet.None);
                    int row = assignments.Rows.Add(character.Name, selected.Text);
                    assignments.Rows[row].Tag = Tuple.Create(account, character, choices);
                }
            assignments.CellValueChanged += AssignmentChanged;
        }

        void AssignmentChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != assignments.Columns["Set"].Index) return;
            var tag = (Tuple<AccountConfig, CharacterConfig, List<Choice>>)assignments.Rows[e.RowIndex].Tag;
            string text = Convert.ToString(assignments.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);
            var choice = tag.Item3.FirstOrDefault(item => item.Text == text);
            if (choice == null) return;
            var preset = ScopePreset;
            // Empty = automatic; "none" = no plugins (see PluginSets.IsAutomatic).
            if (preset == null) tag.Item2.PluginSetId = choice.Id == AutomaticChoice ? null : choice.Id;
            else
            {
                var reference = preset.Characters.First(item => item.CharacterId == tag.Item2.CharacterId && string.Equals(item.AccountId, tag.Item1.Id, StringComparison.OrdinalIgnoreCase));
                reference.PluginSetId = choice.Id == DefaultChoice ? null : choice.Id;
            }
            if (SelectedSet != null) usedBy.Text = UsedByText(SelectedSet);
        }

        void FillRunning()
        {
            var rows = RunningRows?.Invoke() ?? new List<PluginAutoInjector.Row>();
            // The list is refreshed every 3 s; rebuild it only when something changed, so it does not
            // flicker or lose the selection.
            string signature = string.Join("\n", rows.Select(row => row.Pid + "|" + row.CharacterName + "|" + row.SetName + "|" + row.PlayStyle + "|" + row.State + "|" + row.StateText + "|" + row.Detail));
            if (signature == runningSignature && running.Rows.Count > 0) { RefreshCommands(rows.Where(row => row.State == "Loaded").ToList()); return; }
            runningSignature = signature;
            var keep = new HashSet<int>(running.SelectedRows.Cast<DataGridViewRow>().Select(row => (row.Tag as PluginAutoInjector.Row)?.Pid ?? 0));
            running.Rows.Clear();
            foreach (var row in rows)
            {
                var style = PlayStyles.Find(row.PlayStyle);
                int index = running.Rows.Add(row.CharacterName, (row.SetName ?? AOLauncher.Localization.Texts.Get("Text_none_829D1EE4D2A8")) + (style.Id != PlayStyles.None ? " + " + style.Name : ""), row.StateText);
                running.Rows[index].Tag = row;
                bool failed = row.Health.Any(check => check.Status == PluginStatus.Failed || check.Status == PluginStatus.NotLoaded);
                running.Rows[index].Cells[2].Style.ForeColor = row.State == "Loaded" ? (failed ? LogTone.Error : row.HealthSummary != null ? LogTone.Warning : LogTone.Good)
                    : row.State == "Conflict" ? LogTone.Error : row.State == "Failed" || row.State == "External" ? LogTone.Warning : Theme.Muted;
                if (!string.IsNullOrEmpty(row.Detail)) foreach (DataGridViewCell cell in running.Rows[index].Cells) cell.ToolTipText = row.Detail;
                running.Rows[index].Selected = keep.Contains(row.Pid);
            }
            if (rows.Count == 0) { int index = running.Rows.Add(AOLauncher.Localization.Texts.Get("Text_No_characters_from_Nexus_are_in_3D82D5980BF8"), "", ""); running.Rows[index].Tag = null; running.ClearSelection(); }
            inGame.Enabled = rows.Any(row => row.State == "Loaded");
            RefreshCommands(rows.Where(row => row.State == "Loaded").ToList());
        }

        // Asks loaded clients which commands they know, at most every 15 s per client, off the UI thread.
        void RefreshCommands(List<PluginAutoInjector.Row> rows)
        {
            var due = rows.Where(row => !commandsAsked.TryGetValue(row.Pid, out var asked) || (DateTime.UtcNow - asked).TotalSeconds > 15).ToList();
            foreach (var row in due) commandsAsked[row.Pid] = DateTime.UtcNow;
            foreach (int gone in clientCommands.Keys.Where(pid => !rows.Any(row => row.Pid == pid)).ToList()) { clientCommands.Remove(gone); commandsAsked.Remove(gone); }
            if (due.Count == 0) return;
            Task.Run(() => due.Select(row => Tuple.Create(row.Pid, ShortcutClient.Commands(row.Pid))).ToList()).ContinueWith(task =>
            {
                if (IsDisposed || task.IsFaulted) return;
                try { BeginInvoke((Action)(() => { foreach (var item in task.Result) clientCommands[item.Item1] = item.Item2; })); } catch (InvalidOperationException) { }
            });
        }

        List<PluginAutoInjector.Row> SelectedOrAllLoaded()
        {
            var all = running.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as PluginAutoInjector.Row).Where(row => row != null && row.State == "Loaded").ToList();
            var selected = running.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as PluginAutoInjector.Row).Where(row => row != null && row.State == "Loaded").ToList();
            return selected.Count > 0 ? selected : all;
        }

        bool Knows(PluginAutoInjector.Row row, string command)
        {
            string name = command.Split(' ')[0];
            return !clientCommands.TryGetValue(row.Pid, out var known) || known == null ? false : known.Contains(name);
        }

        // The In game menu: team commands, the play style's commands and every loaded plugin's window.
        void ShowInGameMenu()
        {
            var targets = SelectedOrAllLoaded();
            var everyone = running.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as PluginAutoInjector.Row).Where(row => row != null && row.State == "Loaded").ToList();
            var menu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground, ShowImageMargin = false };
            menu.Closed += (sender, e) => BeginInvoke((Action)menu.Dispose);
            bool someSelected = running.SelectedRows.Cast<DataGridViewRow>().Any(row => row.Tag is PluginAutoInjector.Row r && r.State == "Loaded");
            menu.Items.Add(new ToolStripLabel(targets.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_No_character_has_plugins_loaded_AF4B80096082")
                : someSelected ? string.Format(AOLauncher.Localization.Texts.Get("Text_In_0_selected_character_s_27A890D334C0"), targets.Count) : string.Format(AOLauncher.Localization.Texts.Get("Text_In_all_0_characters_981156A88D3E"), targets.Count)) { ForeColor = Theme.Muted });
            Action<GameAction, ToolStripItemCollection> addAction = (action, items) =>
            {
                var item = new ToolStripMenuItem(action.Label + "   /" + action.Command) { ToolTipText = action.Hint };
                if (action.LeaderOnly)
                {
                    item.Text = action.Label + "   /" + action.Command + "  " + AOLauncher.Localization.Texts.Get("Text_on_the_leader_253318C72514");
                    foreach (var row in everyone)
                    {
                        var target = row;
                        var leader = new ToolStripMenuItem(row.CharacterName) { Enabled = Knows(row, action.Command) || !clientCommands.ContainsKey(row.Pid) };
                        leader.Click += async (sender, e) => await RunOn(new List<PluginAutoInjector.Row> { target }, action.Command);
                        item.DropDownItems.Add(leader);
                    }
                    item.Enabled = everyone.Count > 0;
                }
                else
                {
                    item.Enabled = targets.Any(row => Knows(row, action.Command) || !clientCommands.ContainsKey(row.Pid));
                    item.Click += async (sender, e) => await RunOn(targets, action.Command);
                }
                items.Add(item);
            };
            menu.Items.Add(new ToolStripSeparator());
            foreach (var action in PlayStyles.TeamActions) addAction(action, menu.Items);
            var styles = targets.Select(row => PlayStyles.Find(row.PlayStyle)).Where(style => style.Actions.Length > 0).GroupBy(style => style.Id).Select(group => group.First()).ToList();
            foreach (var style in styles)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripLabel(style.Name) { ForeColor = Theme.Muted });
                foreach (var action in style.Actions) addAction(action, menu.Items);
            }
            // Every plugin window the targets have, from the catalog's settings commands.
            var windows = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Open_a_plugin_window_E36930A3474A"));
            foreach (var plugin in Catalog().Where(item => !string.IsNullOrEmpty(item.Settings)).GroupBy(item => item.Settings, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(item => item.Name))
            {
                if (!targets.Any(row => Knows(row, plugin.Settings))) continue;
                var target = plugin;
                var item = new ToolStripMenuItem(plugin.Name + "   /" + plugin.Settings);
                item.Click += async (sender, e) => await RunOn(targets.Where(row => Knows(row, target.Settings)).ToList(), target.Settings);
                windows.DropDownItems.Add(item);
            }
            windows.Enabled = windows.DropDownItems.Count > 0;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(windows);
            var typed = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_Run_a_command_7214742CBD3F")) { Enabled = targets.Count > 0 };
            typed.Click += async (sender, e) =>
            {
                using (var dialog = new TextPromptDialog(AOLauncher.Localization.Texts.Get("Text_Run_a_plugin_command_7963F4503D03"), AOLauncher.Localization.Texts.Get("Text_Command_as_typed_in_the_game_52AEB35E2900"), "/"))
                    if (dialog.ShowDialog(this) == DialogResult.OK) await RunOn(targets, dialog.Value);
            };
            menu.Items.Add(typed);
            menu.Items.Add(new ToolStripSeparator());
            var howTo = new ToolStripMenuItem(AOLauncher.Localization.Texts.Get("Text_How_to_play_6945BB20D7A1"));
            howTo.Click += (sender, e) => ShowHowTo(styles.FirstOrDefault() ?? CurrentStyle);
            menu.Items.Add(howTo);
            menu.Show(inGame, new Point(0, inGame.Height));
        }

        async Task RunOn(List<PluginAutoInjector.Row> targets, string command)
        {
            string line = (command ?? "").Trim().TrimStart('/');
            if (targets.Count == 0 || line.Length == 0) return;
            UseWaitCursor = true;
            var failures = new List<string>();
            try
            {
                foreach (var target in targets)
                {
                    var result = await Task.Run(() => ShortcutClient.Run(target.Pid, line));
                    if (!result.Ok) failures.Add(target.CharacterName + ": " + result.Message);
                }
            }
            finally { UseWaitCursor = false; }
            int ok = targets.Count - failures.Count;
            string summary = string.Format(AOLauncher.Localization.Texts.Get("Text_Ran_0_in_1_of_2_811F7C9F1BF8"), line, ok, targets.Count);
            ErrorLabel.ForeColor = failures.Count == 0 ? Theme.Accent : Theme.Warning;
            ErrorLabel.Text = summary + (failures.Count > 0 ? "  " + string.Join("  ", failures.Take(3)) : "");
            Log?.Invoke(summary + (failures.Count > 0 ? " " + string.Join(" ", failures) : ""));
        }

        void ShowHowTo(PlayStyle style)
        {
            using (var dialog = new HowToPlayDialog(style, Catalog())) dialog.ShowDialog(this);
        }

        string UsedByText(PluginSet set)
        {
            var users = new List<string>();
            foreach (var account in Working.Accounts)
                foreach (var character in account.Characters.Where(c => c.PluginSetId == set.Id)) users.Add(character.Name);
            foreach (var preset in Working.Presets)
                foreach (var reference in preset.Characters.Where(c => c.PluginSetId == set.Id))
                {
                    var character = Working.Accounts.FirstOrDefault(a => a.Id == reference.AccountId)?.Characters.FirstOrDefault(c => c.CharacterId == reference.CharacterId);
                    if (character != null) users.Add(character.Name + AOLauncher.Localization.Texts.Get("Text_in_373D4683A6AB") + preset.Name + ")");
                }
            return users.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_Not_used_by_any_character_yet_888FA82232B3")
                : AOLauncher.Localization.Texts.Get("Text_Used_by_8B990E7086AD") + string.Join(", ", users);
        }

        async Task ActOnRunning(bool load)
        {
            var targets = running.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as PluginAutoInjector.Row).Where(row => row != null).ToList();
            if (targets.Count == 0) { ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Select_one_or_more_characters_first_749948E35E20"); return; }
            ErrorLabel.Text = "";
            Working.AutoInjectPlugins = autoInject.Checked;
            var snapshot = Working;
            var messages = new List<string>();
            UseWaitCursor = true;
            try
            {
                foreach (var target in targets)
                {
                    string message = await Task.Run(() => load ? LoadNow?.Invoke(target, snapshot) : UnloadNow?.Invoke(target));
                    if (!string.IsNullOrEmpty(message)) messages.Add(target.CharacterName + ": " + message);
                }
            }
            finally { UseWaitCursor = false; }
            ErrorLabel.ForeColor = messages.Count == 0 ? Theme.Accent : Theme.Warning;
            ErrorLabel.Text = messages.Count == 0 ? (load ? AOLauncher.Localization.Texts.Get("Text_Plugin_load_requested_Startup_status_appears_E52CF5831505") : AOLauncher.Localization.Texts.Get("Text_Plugins_unloaded_2128001DEB5C")) : string.Join("  ", messages);
            FillRunning();
        }

        void OpenTest()
        {
            var loaded = (RunningRows?.Invoke() ?? new List<PluginAutoInjector.Row>()).Where(row => row.State == "Loaded").ToList();
            var selected = running.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as PluginAutoInjector.Row).FirstOrDefault(row => row != null);
            var preferred = selected == null ? null : loaded.FirstOrDefault(row => row.Pid == selected.Pid);
            using (var dialog = new PluginTestDialog(loaded, Catalog(), Working, preferred)
            {
                SendPlugins = SendPlugins, SetTesting = SetTesting, LoadNow = LoadNow, UnloadNow = UnloadNow,
                SaveResults = records =>
                {
                    foreach (var record in records) Working.PluginTests.RemoveAll(item => string.Equals(item.PluginId, record.PluginId, StringComparison.OrdinalIgnoreCase));
                    Working.PluginTests.AddRange(records);
                    SaveTests?.Invoke(records);
                    grid.Invalidate();
                }
            })
                dialog.ShowDialog(this);
            FillRunning();
        }

        void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = AOLauncher.Localization.Texts.Get("Text_Choose_the_folder_that_contains_AOSharp_8437DD5AE08B"), ShowNewFolderButton = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var candidate = AOSharpPack.FromFolder(dialog.SelectedPath, out string error);
                if (candidate == null) { ErrorLabel.Text = error; return; }
                Working.AOSharpPackPath = candidate.Root; ErrorLabel.Text = "";
                ReloadPack(); ShowSelectedSet();
            }
        }

        void Import()
        {
            using (var dialog = new OpenFileDialog { Filter = "AO# config.json|config.json|JSON|*.json", Title = AOLauncher.Localization.Texts.Get("Text_Choose_the_AO_config_json_185C894E9093") })
            {
                string guess = !string.IsNullOrWhiteSpace(Working.AOSharpPackPath) ? Path.Combine(Working.AOSharpPackPath, "AOSharp") : null;
                if (guess != null && Directory.Exists(guess)) dialog.InitialDirectory = guess;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var result = PluginSets.ImportAOSharpConfig(Working, currentPack ?? pack, dialog.FileName);
                    catalogCache = null;
                    ErrorLabel.ForeColor = Theme.Accent;
                    ErrorLabel.Text = string.Format(AOLauncher.Localization.Texts.Get("Text_Imported_0_new_sets_1_characters_4B5291A8B9C2"), result.Sets, result.Characters, result.CustomPlugins)
                        + (result.Unmatched.Count > 0 ? " " + AOLauncher.Localization.Texts.Get("Text_No_matching_character_0EDDCC64CD69") + string.Join(", ", result.Unmatched.Take(6)) : "");
                    FillSets(); FillAssignments();
                }
                catch (Exception ex) { ErrorLabel.ForeColor = Theme.Warning; ErrorLabel.Text = ex.Message; }
            }
        }

        void AddCustomPlugin()
        {
            using (var dialog = new OpenFileDialog { Filter = AOLauncher.Localization.Texts.Get("Text_Plugin_DLL_2945082DB03C") + "|*.dll", Title = AOLauncher.Localization.Texts.Get("Text_Add_a_plugin_DLL_69E6E690AF0D") })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.FileName.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0) { ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Choose_the_DLL_from_bin_not_39035B757269"); return; }
                if (Working.CustomPlugins.Any(item => string.Equals(item.Path, dialog.FileName, StringComparison.OrdinalIgnoreCase))) return;
                string name;
                try { name = System.Diagnostics.FileVersionInfo.GetVersionInfo(dialog.FileName).ProductName; } catch { name = null; }
                Working.CustomPlugins.Add(new CustomPlugin { Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(dialog.FileName) : name.Trim(), Path = dialog.FileName });
                catalogCache = null;
                ShowSelectedSet();
            }
        }
    }

    internal sealed class TextPromptDialog : EditorDialog
    {
        readonly TextBox input;
        internal string Value => input.Text;
        internal TextPromptDialog(string title, string label, string value) : base(title, 200)
        {
            Fields.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Top });
            input = new NexusTextBox { Text = value ?? "", Dock = DockStyle.Top, MaxLength = 60 };
            Fields.Controls.Add(input);
            SaveButton.Click += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Any(char.IsControl)) { ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Enter_a_name_143AC10A1B35"); return; }
                DialogResult = DialogResult.OK;
            };
            ActiveControl = input;
        }
    }

    // A play style explained: what it does, the steps, the plugins it adds and the commands to type.
    internal sealed class HowToPlayDialog : EditorDialog
    {
        internal HowToPlayDialog(PlayStyle style, List<PluginInfo> catalog) : base(AOLauncher.Localization.Texts.Get("Text_How_to_play_F20C75C66161") + style.Name, 560)
        {
            ClientSize = new Size(620, 560);
            Label Section(string text) { var label = AddWrappedNote(text, null); label.ForeColor = Theme.Foreground; label.Font = new Font("Segoe UI", 9F, FontStyle.Bold); label.Margin = new Padding(0, 10, 0, 2); return label; }
            AddWrappedNote(style.About, "playStyleAbout").ForeColor = Theme.Foreground;
            if (style.Steps.Length > 0)
            {
                Section(AOLauncher.Localization.Texts.Get("Text_STEPS_06D98F24A7BB"));
                for (int i = 0; i < style.Steps.Length; i++) AddWrappedNote((i + 1) + ".  " + style.Steps[i], null).ForeColor = Theme.Foreground;
            }
            var added = style.Z.Select(id => catalog.FirstOrDefault(item => item.Id == "pack:" + id)).Where(item => item != null).ToList();
            if (added.Count > 0)
            {
                Section(AOLauncher.Localization.Texts.Get("Text_PLUGINS_THIS_STYLE_ADDS_Z_SUITE_1F1518314495"));
                foreach (var plugin in added) AddWrappedNote(plugin.Name + (string.IsNullOrEmpty(plugin.About) ? "" : " — " + plugin.About) + (string.IsNullOrEmpty(plugin.Note) ? "" : "  ⚠ " + plugin.Note), null);
                if (style.AOSP.Length == 0) AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_This_style_has_no_AOSP_version_8D0E26EBFBA3"), null).ForeColor = Theme.Amber;
            }
            var lines = new List<string>();
            foreach (var action in PlayStyles.TeamActions.Concat(style.Actions)) lines.Add("/" + action.Command + "   " + action.Label + (action.LeaderOnly ? AOLauncher.Localization.Texts.Get("Text_on_the_leader_6931B4988660") : ""));
            foreach (var plugin in added.Where(item => !string.IsNullOrEmpty(item.Settings))) lines.Add("/" + plugin.Settings + "   " + string.Format(AOLauncher.Localization.Texts.Get("Text_opens_0_D4093398269B"), plugin.Name));
            // One line per command: a style's own action wins over the generic "opens" line.
            lines = lines.GroupBy(line => line.Split(new[] { "   " }, StringSplitOptions.None)[0]).Select(group => group.First()).ToList();
            Section(AOLauncher.Localization.Texts.Get("Text_COMMANDS_TYPE_THEM_IN_THE_GAME_5C6D174AF40B"));
            var box = new TextBox { Multiline = true, ReadOnly = true, Text = string.Join("\r\n", lines), Dock = DockStyle.Top, Height = Math.Min(220, 22 + lines.Count * 18),
                BackColor = LogViewer.TerminalBackground, ForeColor = LogTone.Neutral, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5F), ScrollBars = ScrollBars.Vertical };
            Fields.Controls.Add(box);
            // A read-only TextBox selects all its text when it gets focus; open with nothing selected.
            Shown += (sender, e) => { box.Select(0, 0); SaveButton.Focus(); };
            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175");
            SaveButton.Click += (sender, e) => DialogResult = DialogResult.OK;
            CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Copy_commands_C571203FC93A");
            CancelAction.DialogResult = DialogResult.None;
            CancelAction.Click += (sender, e) =>
            {
                try { Clipboard.SetText(box.Text); ErrorLabel.ForeColor = Theme.Accent; ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Copied_901F50251A04"); }
                catch (Exception ex) { ErrorLabel.Text = ex.Message; }
            };
        }
    }
}
