using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class GuiManagerDialog
    {
        private readonly NexusTabControl scriptModes = new NexusTabControl();
        private readonly NexusTreeView macroTargets = new NexusTreeView();
        private readonly ComboBox backupFilter = new NexusComboBox();
        private readonly Label scriptNextStep = new Label(), macroNextStep = new Label(), backupCount = new Label();
        private readonly TextBox backupPreview = new NexusTextBox();
        private readonly List<BackupChoice> backupChoices = new List<BackupChoice>();
        private Button copyScriptsSelected, copyScriptsAll, deleteScriptsButton, refreshMacrosButton;
        private bool updatingMacroTargets, filteringBackups;

        private static Control WorkflowCombo(ComboBox combo, string name)
        {
            combo.Name = name; combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.Dock = DockStyle.Top;
            var row = new Panel { Dock = DockStyle.Top, Height = 42, Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(combo);
            combo.SizeChanged += (sender, e) => row.Height = combo.Height + 8;
            return row;
        }
        private static void WorkflowStatus(Label label, string name)
        {
            label.Name = name; label.AutoSize = false; label.Height = 48; label.Dock = DockStyle.Fill; label.ForeColor = Theme.Muted;
            label.Margin = new Padding(0, 6, 0, 8);
        }
        private static Control WorkflowSelectorRow(string title, ComboBox combo, string name)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.RowStyles.Add(Fill());
            var heading = SectionHeading(title); heading.Margin = new Padding(0, 7, 16, 0); row.Controls.Add(heading, 0, 0);
            row.Controls.Add(WorkflowCombo(combo, name), 1, 0);
            return row;
        }
        private void BuildScripts()
        {
            var page = Page(AOLauncher.Localization.Texts.Get("Text_Scripts_Macros_F26188B354EF"));
            var root = Stack(Auto(), Fill()); page.Controls.Add(root);
            root.Controls.Add(TaskIntro(AOLauncher.Localization.Texts.Get("Text_Scripts_macros_CCE65BE174FA"), AOLauncher.Localization.Texts.Get("Text_Script_files_are_shared_by_a_3EB2ED82BDFB")), 0, 0);
            scriptModes.Name = "guiScriptModes"; scriptModes.Dock = DockStyle.Fill; root.Controls.Add(scriptModes, 0, 1);
            var filesPage = new TabPage(AOLauncher.Localization.Texts.Get("Text_Script_files_FDBE733662E9")) { BackColor = Theme.Background, Padding = new Padding(8) };
            var macrosPage = new TabPage(AOLauncher.Localization.Texts.Get("Text_Character_macros_DF728E608577")) { BackColor = Theme.Background, Padding = new Padding(8) };
            scriptModes.TabPages.Add(filesPage); scriptModes.TabPages.Add(macrosPage);

            // Script files: source and files → contents → copy, side by side.
            var files = Stack(Fill(), Auto(), Auto()); filesPage.Controls.Add(files);
            foreach (var item in skinTarget.Items.Cast<SkinTargetChoice>()) { scriptTarget.Items.Add(item); scriptCopyTarget.Items.Add(item); }
            scriptTarget.SelectedIndexChanged += (sender, e) => RefreshScripts();
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            content.RowStyles.Add(Fill());
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            files.Controls.Add(content, 0, 0);
            var library = Card(); library.Margin = new Padding(0, 0, 10, 0); content.Controls.Add(library, 0, 0);
            var listStack = Stack(Auto(), Auto(), Auto(), Fill()); library.Controls.Add(listStack);
            listStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_1_Source_installation_9AC637A1AA8A")), 0, 0);
            listStack.Controls.Add(WorkflowCombo(scriptTarget, "guiScriptTarget"), 0, 1);
            listStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_2_Select_script_files_A71CC9B65409")), 0, 2);
            ListStyle(scriptList, "guiScripts"); scriptList.SelectionMode = SelectionMode.MultiExtended; scriptList.SelectedIndexChanged += (sender, e) => ShowScript();
            listStack.Controls.Add(SlimScrolling.Wrap(scriptList), 0, 3);
            // Keep file actions in a full-width row so narrow windows retain a usable list.
            var manage = Actions(); files.Controls.Add(manage, 0, 1);
            scriptActions.Add(ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Import_files_9B6929EE2941"), "importGuiScript", ImportScript));
            deleteScriptsButton = ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Delete_selected_E475C67D6D5F"), "deleteGuiScripts", DeleteScripts); scriptActions.Add(deleteScriptsButton);
            scriptActions.Add(ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Open_folder_159099E40D0E"), "openGuiScripts", OpenScriptFolder));
            scriptActions.Add(ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Refresh_files_315133868B34"), "refreshGuiScripts", () => { RefreshScripts(); return Task.CompletedTask; }));
            var previewCard = Card(); content.Controls.Add(previewCard, 1, 0);
            var previewStack = Stack(Auto(), Fill()); previewCard.Controls.Add(previewStack);
            previewStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_File_contents_read_only_5FE51D1A1A0B")), 0, 0);
            scriptPreview.Name = "guiScriptPreview"; scriptPreview.Dock = DockStyle.Fill; scriptPreview.Multiline = true; scriptPreview.ReadOnly = true;
            scriptPreview.WordWrap = false; scriptPreview.ScrollBars = ScrollBars.Both; scriptPreview.BorderStyle = BorderStyle.None;
            scriptPreview.BackColor = Theme.Surface; scriptPreview.ForeColor = Theme.Foreground; scriptPreview.Font = new Font(FontFamily.GenericMonospace, 9.5F);
            previewStack.Controls.Add(SlimScrolling.Wrap(scriptPreview), 0, 1);
            var copyCard = Card(); copyCard.Margin = Padding.Empty; content.Controls.Add(copyCard, 2, 0);
            var copyStack = Stack(Auto(), Auto(), Auto(), Fill()); copyCard.Controls.Add(copyStack);
            copyStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_3_Copy_to_1398A3B281B7")), 0, 0);
            copyStack.Controls.Add(WorkflowCombo(scriptCopyTarget, "guiScriptCopyTarget"), 0, 1);
            scriptCopyTarget.SelectedIndexChanged += (sender, e) => ScriptWorkflowUpdate();
            var copyActions = Actions(); copyActions.WrapContents = true; copyStack.Controls.Add(copyActions, 0, 2);
            copyScriptsSelected = ActionButton(copyActions, AOLauncher.Localization.Texts.Get("Text_Review_copy_5A6D810BEE5C"), "copyGuiScripts", () => CopyScripts(false), true); scriptActions.Add(copyScriptsSelected);
            copyScriptsAll = ActionButton(copyActions, AOLauncher.Localization.Texts.Get("Text_Copy_to_all_others_F11FAF1639A0"), "copyAllGuiScripts", () => CopyScripts(true)); scriptActions.Add(copyScriptsAll);
            WorkflowStatus(scriptNextStep, "guiScriptNextStep"); scriptNextStep.Dock = DockStyle.Fill; copyStack.Controls.Add(scriptNextStep, 0, 3);
            WorkflowStatus(scriptInfo, "guiScriptInfo"); scriptInfo.Height = 18; scriptInfo.Margin = new Padding(0, 4, 0, 0); files.Controls.Add(scriptInfo, 0, 2);
            if (scriptTarget is NexusComboBox sourceBox) sourceBox.MatchHeight(copyScriptsSelected);
            if (scriptCopyTarget is NexusComboBox destinationBox) destinationBox.MatchHeight(copyScriptsSelected);

            // Character macros: source → characters → copy.
            var macros = Stack(Fill()); macrosPage.Controls.Add(macros);
            foreach (var choice in characters) macroSource.Items.Add(choice);
            macroSource.SelectedIndexChanged += (sender, e) => { RefreshMacros(); RefreshMacroTargets(); };
            var macroColumns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            macroColumns.RowStyles.Add(Fill());
            macroColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); macroColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); macroColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
            macros.Controls.Add(macroColumns, 0, 0);
            var macroCard = Card(); macroCard.Margin = new Padding(0, 0, 10, 0); macroColumns.Controls.Add(macroCard, 0, 0);
            var macroStack = Stack(Auto(), Auto(), Auto(), Fill(), Auto()); macroCard.Controls.Add(macroStack);
            macroStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_1_Source_character_25ACE9133134")), 0, 0);
            macroStack.Controls.Add(WorkflowCombo(macroSource, "guiMacroSource"), 0, 1);
            var savedHeading = Note(AOLauncher.Localization.Texts.Get("Text_Saved_macros_from_last_logout_2BBB8E88F820")); savedHeading.Margin = new Padding(0, 4, 0, 4);
            macroStack.Controls.Add(savedHeading, 0, 2);
            ListStyle(macroList, "guiMacros"); macroList.SelectionMode = SelectionMode.None; macroStack.Controls.Add(SlimScrolling.Wrap(macroList), 0, 3);
            WorkflowStatus(macroInfo, "guiMacroInfo"); macroInfo.Height = 34; macroStack.Controls.Add(macroInfo, 0, 4);
            var targetsCard = Card(); macroColumns.Controls.Add(targetsCard, 1, 0);
            var targetStack = Stack(Auto(), Fill()); targetsCard.Controls.Add(targetStack);
            targetStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_2_Choose_characters_to_replace_macros_38E7AD059E62")), 0, 0);
            macroTargets.Name = "guiMacroTargets"; macroTargets.Dock = DockStyle.Fill; macroTargets.CheckBoxes = true; macroTargets.BorderStyle = BorderStyle.None;
            macroTargets.BackColor = Theme.Surface; macroTargets.ForeColor = Theme.Foreground; macroTargets.ItemHeight = 28; macroTargets.HideSelection = false;
            macroTargets.AfterCheck += (sender, e) => {
                if (updatingMacroTargets) return;
                updatingMacroTargets = true;
                try {
                    foreach (var child in Descendants(e.Node)) child.Checked = e.Node.Checked;
                    for (var parent = e.Node.Parent; parent != null; parent = parent.Parent) parent.Checked = parent.Nodes.Cast<TreeNode>().All(node => node.Checked);
                }
                finally { updatingMacroTargets = false; }
                ScriptWorkflowUpdate();
            };
            targetStack.Controls.Add(SlimScrolling.Wrap(macroTargets), 0, 1);
            var macroActionCard = Card(); macroActionCard.Margin = Padding.Empty; macroColumns.Controls.Add(macroActionCard, 2, 0);
            var macroActionStack = Stack(Auto(), Auto(), Fill()); macroActionCard.Controls.Add(macroActionStack);
            macroActionStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_3_Copy_the_macros_DA90CC166983")), 0, 0);
            var macroActions = Actions(); macroActions.WrapContents = true; macroActionStack.Controls.Add(macroActions, 0, 1);
            copyMacros = ActionButton(macroActions, AOLauncher.Localization.Texts.Get("Text_Review_macro_copy_E585DA5105BB"), "copyGuiMacros", CopyMacros, true);
            refreshMacrosButton = ActionButton(macroActions, AOLauncher.Localization.Texts.Get("Text_Refresh_saved_macros_7BD1844C22E4"), "refreshGuiMacros", () => { RefreshMacros(); return Task.CompletedTask; });
            if (macroSource is NexusComboBox macroBox) macroBox.MatchHeight(copyMacros);
            WorkflowStatus(macroNextStep, "guiMacroNextStep"); macroNextStep.Dock = DockStyle.Fill; macroActionStack.Controls.Add(macroNextStep, 0, 2);
            if (scriptTarget.Items.Count > 0) scriptTarget.SelectedIndex = 0;
            if (scriptCopyTarget.Items.Count > 1) scriptCopyTarget.SelectedIndex = 1; else if (scriptCopyTarget.Items.Count > 0) scriptCopyTarget.SelectedIndex = 0;
            if (macroSource.Items.Count > 0) macroSource.SelectedIndex = 0;
        }

        private void RefreshMacroTargets()
        {
            var previous = new HashSet<string>(SelectedMacroTargets().Select(choice => choice.CharacterPath), StringComparer.OrdinalIgnoreCase);
            var source = macroSource.SelectedItem as GuiCharacterChoice;
            updatingMacroTargets = true; macroTargets.BeginUpdate();
            try {
                macroTargets.Nodes.Clear();
                foreach (var account in characters.Where(choice => source == null || !string.Equals(choice.CharacterPath, source.CharacterPath, StringComparison.OrdinalIgnoreCase)).GroupBy(choice => choice.Account.Id)) {
                    var parent = new TreeNode(account.First().Account.Label + " · " + AccountTargets.ServerName(settings, account.First().Account));
                    foreach (var choice in account) parent.Nodes.Add(new TreeNode(choice.Character.Name) { Tag = choice, Checked = previous.Contains(choice.CharacterPath) });
                    parent.Checked = parent.Nodes.Cast<TreeNode>().All(node => node.Checked);
                    macroTargets.Nodes.Add(parent);
                }
                macroTargets.ExpandAll();
            } finally { macroTargets.EndUpdate(); updatingMacroTargets = false; }
            ScriptWorkflowUpdate();
        }
        private List<GuiCharacterChoice> SelectedMacroTargets() => macroTargets.Nodes.Cast<TreeNode>().SelectMany(Descendants)
            .Where(node => node.Checked && node.Tag is GuiCharacterChoice).Select(node => (GuiCharacterChoice)node.Tag).ToList();

        private void ScriptWorkflowUpdate()
        {
            if (copyScriptsSelected == null || copyMacros == null) return;
            var source = scriptTarget.SelectedItem as SkinTargetChoice;
            var destination = scriptCopyTarget.SelectedItem as SkinTargetChoice;
            int selected = scriptList.SelectedItems.Count;
            bool different = source != null && destination != null && !string.Equals(source.Root, destination.Root, StringComparison.OrdinalIgnoreCase);
            copyScriptsSelected.Enabled = !busy && selected > 0 && different;
            copyScriptsAll.Enabled = !busy && selected > 0 && source != null && scriptTarget.Items.Count > 1;
            deleteScriptsButton.Enabled = !busy && selected > 0 && source != null;
            scriptNextStep.Text = source == null ? AOLauncher.Localization.Texts.Get("Text_Set_an_account_s_saved_preferences_67D04E6708B1")
                : scriptList.Items.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_No_script_files_here_yet_Import_E3C50127729C")
                : selected == 0 ? AOLauncher.Localization.Texts.Get("Text_Select_files_in_the_list_Ctrl_52BF03F82CFF")
                : !different ? AOLauncher.Localization.Texts.Get("Text_Choose_a_different_destination_installation_Scripts_4A60B6A60D14")
                : selected + AOLauncher.Localization.Texts.Get("Text_selected_review_before_replacing_files_backup_F08AE4B67164");
            if (scriptList.Items.Count == 0 && source != null) scriptPreview.Text = AOLauncher.Localization.Texts.Get("Text_Select_or_import_a_script_to_7D1635EF0399");
            var macroSourceChoice = macroSource.SelectedItem as GuiCharacterChoice;
            var selectedMacros = SelectedMacroTargets();
            bool missing = selectedMacros.Any(choice => string.IsNullOrEmpty(choice.CharacterPath) || !Directory.Exists(choice.CharacterPath));
            copyMacros.Enabled = !busy && macroSourceChoice != null && macroList.Items.Count > 0 && selectedMacros.Count > 0 && !missing;
            if (refreshMacrosButton != null) refreshMacrosButton.Enabled = !busy && macroSourceChoice != null;
            macroNextStep.Text = macroSourceChoice == null ? AOLauncher.Localization.Texts.Get("Text_Add_a_character_to_Nexus_to_4C7FC92C932E")
                : macroList.Items.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_No_saved_macros_on_this_character_D9F9BB732372")
                : missing ? AOLauncher.Localization.Texts.Get("Text_Some_selected_characters_have_no_saved_FA91D385D603")
                : selectedMacros.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_Choose_destination_characters_Their_entire_macro_AFF7BA780D76")
                : selectedMacros.Count + AOLauncher.Localization.Texts.Get("Text_character_s_selected_replaces_all_macros_C84C5ADE1612");
            if (macroList.Items.Count > 0) macroNextStep.Text += "\r\n" + AOLauncher.Localization.Texts.Get("Text_Hotbars_are_kept_Existing_macro_buttons_E4B512F3B157");
        }

        private async Task CopyMacroSelection()
        {
            var source = macroSource.SelectedItem as GuiCharacterChoice;
            var selected = UniqueTargets(SelectedMacroTargets()).Where(choice => source != null && !string.Equals(choice.CharacterPath, source.CharacterPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (source == null || selected.Count == 0) return;
            if (preview) { Status(AOLauncher.Localization.Texts.Get("Text_Preview_only_No_macros_were_copied_A293E7BF5A5B")); return; }
            var involved = new List<GuiCharacterChoice> { source }; involved.AddRange(selected);
            CaptureSource = source;
            try {
                string description = AOLauncher.Localization.Texts.Get("Text_Copy_all_macros_from_88020E374D89") + source.Character.Name + AOLauncher.Localization.Texts.Get("Text_to_5F21D7F5EBF6") + selected.Count + AOLauncher.Localization.Texts.Get("Text_character_s_6DAB9705B325")
                    + GuiComponentChoices.Describe(GuiLayoutComponents.Macros) + "\r\n" + AOLauncher.Localization.Texts.Get("Text_Hotbars_are_not_copied_Existing_macro_F9512F93DB64");
                if (await Run(description, involved, false, () => {
                    var template = GuiLayouts.CaptureWithOptions(libraryRoot, "Macro copy", source.CharacterPath, source.Character.Name, "", GuiLayoutComponents.Macros);
                    try { ApplyLayoutBatch(template.Id, selected, GuiLayoutComponents.Macros); }
                    finally { GuiLayouts.DeleteTemplate(libraryRoot, template.Id); }
                })) Status(AOLauncher.Localization.Texts.Get("Text_Macros_copied_Previous_macro_sets_are_C3A836F9636D"));
            } finally { CaptureSource = null; RefreshBackups(); RefreshMacros(); }
        }

        private void BuildBackups()
        {
            var page = Page(AOLauncher.Localization.Texts.Get("Text_Backups_F7E7530A6198")); var root = Stack(Auto(), Fill()); page.Controls.Add(root);
            root.Controls.Add(TaskIntro(AOLauncher.Localization.Texts.Get("Text_Undo_a_previous_change_AB050510BC81"), AOLauncher.Localization.Texts.Get("Text_Nexus_saves_the_previous_settings_before_9FE82F3FDB40")), 0, 0);
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            backupFilter.Items.AddRange(new object[] { AOLauncher.Localization.Texts.Get("Text_All_changes_A36CAD1C531C"), AOLauncher.Localization.Texts.Get("Text_Layouts_macros_31421C82F2F5"), AOLauncher.Localization.Texts.Get("Text_Skins_A6432A47B0A5"), AOLauncher.Localization.Texts.Get("Text_Scripts_DF361AC73944") });
            filterRow.Controls.Add(WorkflowCombo(backupFilter, "guiBackupFilter"), 0, 0);
            WorkflowStatus(backupCount, "guiBackupCount"); backupCount.Margin = new Padding(12, 8, 0, 0); filterRow.Controls.Add(backupCount, 1, 0);
            var refresh = Actions(); refresh.Margin = Padding.Empty; filterRow.Controls.Add(refresh, 2, 0);
            ActionButton(refresh, AOLauncher.Localization.Texts.Get("Text_Refresh_A97CB8A8CE4A"), "refreshGuiBackups", () => { RefreshBackups(); return Task.CompletedTask; });
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.RowStyles.Add(Fill());
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53)); root.Controls.Add(columns, 0, 1);
            var history = Card(); history.Margin = new Padding(0, 0, 12, 0); columns.Controls.Add(history, 0, 0);
            var list = Stack(Auto(), Auto(), Fill()); history.Controls.Add(list); list.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_1_Choose_a_saved_change_105BFDAC941A")), 0, 0);
            filterRow.Margin = new Padding(0, 0, 0, 8); list.Controls.Add(filterRow, 0, 1);
            backups.Name = "guiBackups"; backups.Dock = DockStyle.Fill; backups.IntegralHeight = false; backups.BorderStyle = BorderStyle.None;
            backups.BackColor = Theme.Surface; backups.ForeColor = Theme.Foreground; backups.DrawMode = DrawMode.OwnerDrawFixed;
            Action sizeRows = () => backups.ItemHeight = TextRenderer.MeasureText("Ag", backups.Font).Height * 2 + 14;
            backups.FontChanged += (sender, e) => sizeRows(); sizeRows();
            backups.DrawItem += (sender, e) => {
                if (e.Index < 0 || e.Index >= backups.Items.Count) return;
                var entry = (BackupChoice)backups.Items[e.Index];
                using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                string kind = entry.Layout != null ? entry.Layout.Components == GuiLayoutComponents.Macros ? AOLauncher.Localization.Texts.Get("Text_Macros_38AD5680493D") : AOLauncher.Localization.Texts.Get("Text_Layout_6A3A0108F12C") : entry.Script != null ? AOLauncher.Localization.Texts.Get("Text_Scripts_DF361AC73944") : AOLauncher.Localization.Texts.Get("Text_Skin_0CE923556F5B");
                int lineHeight = TextRenderer.MeasureText("Ag", backups.Font).Height;
                TextRenderer.DrawText(e.Graphics, kind + " · " + entry.When.ToLocalTime().ToString("g"), backups.Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 5, Math.Max(1, e.Bounds.Width - 16), lineHeight), Theme.Foreground, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, entry.Layout != null ? entry.Character.Label : entry.SkinTarget.ToString(), backups.Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y + lineHeight + 8, Math.Max(1, e.Bounds.Width - 16), lineHeight), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            backups.SelectedIndexChanged += (sender, e) => { if (!filteringBackups) UpdateSelection(); }; list.Controls.Add(SlimScrolling.Wrap(backups), 0, 2);
            var details = Card(); details.Margin = Padding.Empty; columns.Controls.Add(details, 1, 0);
            var detailStack = Stack(Auto(), Fill(), Auto(), Auto()); details.Controls.Add(detailStack);
            detailStack.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_2_Review_what_will_be_restored_2D9F3F59EA96")), 0, 0);
            backupPreview.Name = "guiBackupPreview"; backupPreview.Dock = DockStyle.Fill; backupPreview.Multiline = true; backupPreview.ReadOnly = true;
            backupPreview.ScrollBars = ScrollBars.Vertical; backupPreview.BorderStyle = BorderStyle.None; backupPreview.BackColor = Theme.Surface; backupPreview.ForeColor = Theme.Foreground;
            detailStack.Controls.Add(SlimScrolling.Wrap(backupPreview), 0, 1);
            WorkflowStatus(backupInfo, "guiBackupInfo"); detailStack.Controls.Add(backupInfo, 0, 2);
            var action = Actions(); detailStack.Controls.Add(action, 0, 3);
            restoreBackup = ActionButton(action, AOLauncher.Localization.Texts.Get("Text_Review_restore_5A36E27CD214"), "restoreGuiBackup", RestoreBackup, true);
            backupFilter.SelectedIndexChanged += (sender, e) => FilterBackups(); backupFilter.SelectedIndex = 0;
        }
        private static string BackupKey(BackupChoice choice) => choice == null ? null : choice.Layout != null ? "layout:" + choice.Layout.Id
            : choice.Script != null ? "script:" + choice.Script.Id : "skin:" + choice.Skin.Directory;
        private void FilterBackups()
        {
            string selected = BackupKey(backups.SelectedItem as BackupChoice); filteringBackups = true; backups.BeginUpdate();
            try {
                backups.Items.Clear(); int filter = backupFilter.SelectedIndex;
                foreach (var entry in backupChoices.Where(entry => filter <= 0 || filter == 1 && entry.Layout != null || filter == 2 && entry.Skin != null || filter == 3 && entry.Script != null)) backups.Items.Add(entry);
                backups.SelectedItem = backups.Items.Cast<BackupChoice>().FirstOrDefault(entry => BackupKey(entry) == selected);
                if (backups.SelectedIndex < 0 && backups.Items.Count > 0) backups.SelectedIndex = 0;
            } finally { backups.EndUpdate(); filteringBackups = false; }
            UpdateSelection();
        }
        private void BackupWorkflowUpdate()
        {
            if (restoreBackup == null) return;
            backupCount.Text = backups.Items.Count + AOLauncher.Localization.Texts.Get("Text_saved_changes_newest_first_C6F65DB41D07");
            var choice = backups.SelectedItem as BackupChoice;
            if (choice == null) {
                backupInfo.Text = backupChoices.Count == 0 ? AOLauncher.Localization.Texts.Get("Text_No_backups_yet_Your_first_change_63B2BBDE4BAD") : AOLauncher.Localization.Texts.Get("Text_No_saved_changes_match_this_filter_F5DDE84E04E3");
                backupPreview.Text = AOLauncher.Localization.Texts.Get("Text_Your_recovery_history_will_appear_here_BCDD5B2AF10E");
                return;
            }
            var text = new StringBuilder();
            text.AppendLine(AOLauncher.Localization.Texts.Get("Text_Saved_before_the_change_A326DF0C7AD5")).AppendLine(choice.When.ToLocalTime().ToString("f")).AppendLine();
            string state = choice.Layout?.State ?? choice.Script?.State;
            if (state != null) text.AppendLine(BackupStateText(state)).AppendLine();
            else if (!choice.Skin.SelectionCompleted) text.AppendLine(AOLauncher.Localization.Texts.Get("Text_The_skin_change_did_not_finish_FB23D8AE80D2")).AppendLine();
            text.AppendLine(AOLauncher.Localization.Texts.Get("Text_Restore_to_DF67F5E9D077"));
            if (choice.Layout != null) {
                text.AppendLine(choice.Character.Label).AppendLine().AppendLine(AOLauncher.Localization.Texts.Get("Text_Settings_restored_6257AB383061"));
                text.AppendLine(GuiComponentChoices.Describe(choice.Layout.Components).Trim()).AppendLine();
                text.AppendLine(AOLauncher.Localization.Texts.Get("Text_Other_character_settings_are_kept_where_B45A09E72555"));
                backupInfo.Text = AOLauncher.Localization.Texts.Get("Text_Original_character_only_Nexus_reviews_any_DAC21CF4A1C3");
            } else if (choice.Script != null) {
                text.AppendLine(choice.SkinTarget.ToString()).AppendLine().AppendLine(AOLauncher.Localization.Texts.Get("Text_Undo_script_change_ADFBA23780BC") + ScriptAction(choice.Script.Action));
                foreach (var change in choice.Script.Changes) text.AppendLine("• " + change.Name);
                backupInfo.Text = AOLauncher.Localization.Texts.Get("Text_Current_files_are_backed_up_first_452339BF2523");
            } else {
                text.AppendLine(choice.SkinTarget.ToString()).AppendLine().AppendLine(AOLauncher.Localization.Texts.Get("Text_Skin_to_reactivate_EE34A753C0BB")).AppendLine(choice.Skin.SkinName).AppendLine();
                text.AppendLine(AOLauncher.Localization.Texts.Get("Text_Changes_the_shared_skin_selection_Custom_F4D797B15298"));
                backupInfo.Text = AOLauncher.Localization.Texts.Get("Text_All_accounts_sharing_this_installation_are_8E1291315B62");
            }
            backupPreview.Text = text.ToString();
        }
        private static string BackupStateText(string state)
        {
            switch (state) {
                case "Applied": return AOLauncher.Localization.Texts.Get("Text_Change_completed_previous_state_saved_6DED6181073F");
                case "Restored": return AOLauncher.Localization.Texts.Get("Text_This_saved_change_has_already_been_203D9D4E885C");
                case "Rolled back": return AOLauncher.Localization.Texts.Get("Text_The_failed_change_was_rolled_back_BAD66364C691");
                case "Recovery required": return AOLauncher.Localization.Texts.Get("Text_Recovery_needed_the_earlier_change_could_8489F1A7F528");
                case "Applying": case "Restoring": return AOLauncher.Localization.Texts.Get("Text_Incomplete_operation_review_this_recovery_record_607A94E14C39");
                default: return AOLauncher.Localization.Texts.Get("Text_Recorded_status_CBDAD0B79F13") + state;
            }
        }
    }
}
