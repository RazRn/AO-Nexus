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
            var page = Page(L("Scripts", "Scripts"));
            var root = Stack(Auto(), Fill()); page.Controls.Add(root);
            root.Controls.Add(TaskIntro(L("Scripts & macros", "Scripts og makroer"), L("Script files are shared by a game installation. Macro buttons belong to one character. Choose what you want to manage below.", "Script-filer deles af en spilinstallation. Makroknapper tilhører én char. Vælg nedenfor, hvad du vil arbejde med.")), 0, 0);
            scriptModes.Name = "guiScriptModes"; scriptModes.Dock = DockStyle.Fill; root.Controls.Add(scriptModes, 0, 1);
            var filesPage = new TabPage(L("Script files", "Script-filer")) { BackColor = Theme.Background, Padding = new Padding(8) };
            var macrosPage = new TabPage(L("Character macros", "Char-makroer")) { BackColor = Theme.Background, Padding = new Padding(8) };
            scriptModes.TabPages.Add(filesPage); scriptModes.TabPages.Add(macrosPage);

            var files = Stack(Auto(), Fill(), Auto(), Auto(), Auto()); filesPage.Controls.Add(files);
            files.Controls.Add(WorkflowSelectorRow(L("1  Source installation", "1  Kildeinstallation"), scriptTarget, "guiScriptTarget"), 0, 0);
            foreach (var item in skinTarget.Items.Cast<SkinTargetChoice>()) { scriptTarget.Items.Add(item); scriptCopyTarget.Items.Add(item); }
            scriptTarget.SelectedIndexChanged += (sender, e) => RefreshScripts();
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            content.RowStyles.Add(Fill());
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); files.Controls.Add(content, 0, 1);
            var library = Card(); library.Margin = new Padding(0, 0, 12, 0); content.Controls.Add(library, 0, 0);
            var listStack = Stack(Auto(), Fill()); library.Controls.Add(listStack);
            listStack.Controls.Add(SectionHeading(L("2  Select script files", "2  Vælg script-filer")), 0, 0);
            ListStyle(scriptList, "guiScripts"); scriptList.SelectionMode = SelectionMode.MultiExtended; scriptList.SelectedIndexChanged += (sender, e) => ShowScript();
            listStack.Controls.Add(SlimScrolling.Wrap(scriptList), 0, 1);
            var manage = Actions(); files.Controls.Add(manage, 0, 2);
            scriptActions.Add(ActionButton(manage, L("Import files…", "Importér filer…"), "importGuiScript", ImportScript));
            deleteScriptsButton = ActionButton(manage, L("Delete selected…", "Slet valgte…"), "deleteGuiScripts", DeleteScripts); scriptActions.Add(deleteScriptsButton);
            scriptActions.Add(ActionButton(manage, L("Open folder", "Åbn mappe"), "openGuiScripts", OpenScriptFolder));
            scriptActions.Add(ActionButton(manage, L("Refresh files", "Opdatér filer"), "refreshGuiScripts", () => { RefreshScripts(); return Task.CompletedTask; }));
            var previewCard = Card(); previewCard.Margin = Padding.Empty; content.Controls.Add(previewCard, 1, 0);
            var previewStack = Stack(Auto(), Fill()); previewCard.Controls.Add(previewStack);
            previewStack.Controls.Add(SectionHeading(L("File contents · read only", "Filindhold · skrivebeskyttet")), 0, 0);
            scriptPreview.Name = "guiScriptPreview"; scriptPreview.Dock = DockStyle.Fill; scriptPreview.Multiline = true; scriptPreview.ReadOnly = true;
            scriptPreview.WordWrap = false; scriptPreview.ScrollBars = ScrollBars.Both; scriptPreview.BorderStyle = BorderStyle.None;
            scriptPreview.BackColor = Theme.Surface; scriptPreview.ForeColor = Theme.Foreground; scriptPreview.Font = new Font(FontFamily.GenericMonospace, 9.5F);
            previewStack.Controls.Add(SlimScrolling.Wrap(scriptPreview), 0, 1);
            WorkflowStatus(scriptInfo, "guiScriptInfo"); scriptInfo.Height = 18; scriptInfo.Margin = new Padding(0, 2, 0, 4); files.Controls.Add(scriptInfo, 0, 3);
            var copyArea = Stack(new RowStyle(SizeType.Absolute, 46), Fill()); copyArea.Dock = DockStyle.Top; copyArea.Height = 80; files.Controls.Add(copyArea, 0, 4);
            var destination = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            destination.RowStyles.Add(Fill());
            destination.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var copyHeading = SectionHeading(L("3  Copy to", "3  Kopiér til")); copyHeading.Margin = new Padding(0, 7, 12, 0); destination.Controls.Add(copyHeading, 0, 0);
            destination.Controls.Add(WorkflowCombo(scriptCopyTarget, "guiScriptCopyTarget"), 1, 0);
            scriptCopyTarget.SelectedIndexChanged += (sender, e) => ScriptWorkflowUpdate();
            var copyActions = Actions(); copyActions.WrapContents = false; copyActions.Margin = new Padding(10, 0, 0, 0); destination.Controls.Add(copyActions, 2, 0);
            copyScriptsSelected = ActionButton(copyActions, L("Review copy…", "Gennemse kopi…"), "copyGuiScripts", () => CopyScripts(false), true); scriptActions.Add(copyScriptsSelected);
            copyScriptsAll = ActionButton(copyActions, L("Copy to all others…", "Kopiér til alle andre…"), "copyAllGuiScripts", () => CopyScripts(true)); scriptActions.Add(copyScriptsAll);
            copyArea.Controls.Add(destination, 0, 0); WorkflowStatus(scriptNextStep, "guiScriptNextStep"); scriptNextStep.Height = 30; scriptNextStep.Margin = new Padding(0, 2, 0, 0); copyArea.Controls.Add(scriptNextStep, 0, 1);
            if (scriptTarget is NexusComboBox sourceBox) sourceBox.MatchHeight(copyScriptsSelected);
            if (scriptCopyTarget is NexusComboBox destinationBox) destinationBox.MatchHeight(copyScriptsSelected);

            var macros = Stack(Auto(), Fill(), Auto()); macrosPage.Controls.Add(macros);
            macros.Controls.Add(WorkflowSelectorRow(L("1  Source character", "1  Kildekarakter"), macroSource, "guiMacroSource"), 0, 0);
            foreach (var choice in characters) macroSource.Items.Add(choice);
            macroSource.SelectedIndexChanged += (sender, e) => { RefreshMacros(); RefreshMacroTargets(); };
            var macroColumns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            macroColumns.RowStyles.Add(Fill());
            macroColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44)); macroColumns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); macros.Controls.Add(macroColumns, 0, 1);
            var macroCard = Card(); macroCard.Margin = new Padding(0, 0, 12, 0); macroColumns.Controls.Add(macroCard, 0, 0);
            var macroStack = Stack(Auto(), Fill(), Auto()); macroCard.Controls.Add(macroStack);
            macroStack.Controls.Add(SectionHeading(L("Saved macros · from last logout", "Gemte makroer · fra seneste logout")), 0, 0);
            ListStyle(macroList, "guiMacros"); macroList.SelectionMode = SelectionMode.None; macroStack.Controls.Add(SlimScrolling.Wrap(macroList), 0, 1);
            WorkflowStatus(macroInfo, "guiMacroInfo"); macroInfo.Height = 34; macroStack.Controls.Add(macroInfo, 0, 2);
            var targetsCard = Card(); targetsCard.Margin = Padding.Empty; macroColumns.Controls.Add(targetsCard, 1, 0);
            var targetStack = Stack(Auto(), Fill(), Auto()); targetsCard.Controls.Add(targetStack);
            targetStack.Controls.Add(SectionHeading(L("2  Choose characters to replace macros on", "2  Vælg chars, hvis makroer skal erstattes")), 0, 0);
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
            var macroActions = Actions(); targetStack.Controls.Add(macroActions, 0, 2);
            copyMacros = ActionButton(macroActions, L("Review macro copy…", "Gennemse makrokopi…"), "copyGuiMacros", CopyMacros, true);
            refreshMacrosButton = ActionButton(macroActions, L("Refresh saved macros", "Opdatér gemte makroer"), "refreshGuiMacros", () => { RefreshMacros(); return Task.CompletedTask; });
            if (macroSource is NexusComboBox macroBox) macroBox.MatchHeight(copyMacros);
            WorkflowStatus(macroNextStep, "guiMacroNextStep"); macros.Controls.Add(macroNextStep, 0, 2);
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
            scriptNextStep.Text = source == null ? L("Set an account's saved preferences folder in Edit account to find its scripts.", "Vælg kontoens gemte indstillingsmappe under Rediger konto for at finde scripts.")
                : scriptList.Items.Count == 0 ? L("No script files here yet. Import files or open the folder to add one. Run a script in game with /name.", "Ingen script-filer her endnu. Importér filer eller åbn mappen for at tilføje en. Kør et script i spillet med /navn.")
                : selected == 0 ? L("Select files in the list. Ctrl-click selects several. Changes are backed up and need no relog.", "Vælg filer på listen. Ctrl-klik vælger flere. Ændringer sikkerhedskopieres og kræver ikke genlogin.")
                : !different ? L("Choose a different destination installation. Scripts are shared by every character using it.", "Vælg en anden målinstallation. Scripts deles af alle chars, der bruger den.")
                : selected + L(" selected · review before replacing files · backup kept · no relog needed", " valgt · gennemse før filer erstattes · sikkerhedskopi gemmes · intet genlogin nødvendigt");
            if (scriptList.Items.Count == 0 && source != null) scriptPreview.Text = L("Select or import a script to read its contents here.\r\n\r\nA script is a text file you run in chat with /name.", "Vælg eller importér et script for at læse indholdet her.\r\n\r\nEt script er en tekstfil, du kører i chatten med /navn.");
            var macroSourceChoice = macroSource.SelectedItem as GuiCharacterChoice;
            var selectedMacros = SelectedMacroTargets();
            bool missing = selectedMacros.Any(choice => string.IsNullOrEmpty(choice.CharacterPath) || !Directory.Exists(choice.CharacterPath));
            copyMacros.Enabled = !busy && macroSourceChoice != null && macroList.Items.Count > 0 && selectedMacros.Count > 0 && !missing;
            if (refreshMacrosButton != null) refreshMacrosButton.Enabled = !busy && macroSourceChoice != null;
            macroNextStep.Text = macroSourceChoice == null ? L("Add a character to Nexus to view saved macros.", "Tilføj en char til Nexus for at se gemte makroer.")
                : macroList.Items.Count == 0 ? L("No saved macros on this character. Create them in game with /macro, log out normally, then refresh saved macros.", "Ingen gemte makroer på denne char. Opret dem i spillet med /macro, log normalt ud, og opdatér gemte makroer.")
                : missing ? L("Some selected characters have no saved preferences. Log them in and out once and select their account folder before copying.", "Nogle valgte chars har ingen gemte indstillinger. Log dem ind og ud én gang, og vælg deres kontomappe før kopiering.")
                : selectedMacros.Count == 0 ? L("Choose destination characters. Their entire macro set will be replaced; a backup is kept. Layout selections are separate.", "Vælg mål-chars. Alle deres makroer erstattes; en sikkerhedskopi gemmes. Layoutvalg er separate.")
                : selectedMacros.Count + L(" character(s) selected · replaces all macros · backup kept · running clients may need to relog", " chars valgt · erstatter alle makroer · sikkerhedskopi gemmes · aktive klienter kan kræve genlogin");
            if (macroList.Items.Count > 0) macroNextStep.Text += "\r\n" + L("Hotbars are kept. Existing macro buttons may run different macros after this copy.", "Hotbars bevares. Eksisterende makroknapper kan køre andre makroer efter kopieringen.");
        }

        private async Task CopyMacroSelection()
        {
            var source = macroSource.SelectedItem as GuiCharacterChoice;
            var selected = UniqueTargets(SelectedMacroTargets()).Where(choice => source != null && !string.Equals(choice.CharacterPath, source.CharacterPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (source == null || selected.Count == 0) return;
            if (preview) { Status(L("Preview only. No macros were copied.", "Kun forhåndsvisning. Ingen makroer blev kopieret.")); return; }
            var involved = new List<GuiCharacterChoice> { source }; involved.AddRange(selected);
            CaptureSource = source;
            try {
                string description = L("Copy all macros from ", "Kopiér alle makroer fra ") + source.Character.Name + L(" to ", " til ") + selected.Count + L(" character(s).", " chars.")
                    + GuiComponentChoices.Describe(GuiLayoutComponents.Macros) + "\r\n" + L("Hotbars are not copied. Existing macro buttons may run different macros afterward.", "Hotbars kopieres ikke. Eksisterende makroknapper kan køre andre makroer bagefter.");
                if (await Run(description, involved, false, () => {
                    var template = GuiLayouts.CaptureWithOptions(libraryRoot, "Macro copy", source.CharacterPath, source.Character.Name, "", GuiLayoutComponents.Macros);
                    try { ApplyLayoutBatch(template.Id, selected, GuiLayoutComponents.Macros); }
                    finally { GuiLayouts.DeleteTemplate(libraryRoot, template.Id); }
                })) Status(L("Macros copied. Previous macro sets are available in Backups.", "Makroer kopieret. Tidligere makroer findes under Sikkerhedskopier."));
            } finally { CaptureSource = null; RefreshBackups(); RefreshMacros(); }
        }

        private void BuildBackups()
        {
            var page = Page(L("Backups", "Sikkerhedskopier")); var root = Stack(Auto(), Auto(), Fill()); page.Controls.Add(root);
            root.Controls.Add(TaskIntro(L("Undo a previous change", "Fortryd en tidligere ændring"), L("Nexus saves the previous settings before changing layouts, macros, scripts or skins. Restore returns only the original character or installation to that saved state.", "Nexus gemmer tidligere indstillinger før ændringer af layouts, makroer, scripts eller skins. Gendannelse fører kun den oprindelige char eller installation tilbage til den gemte tilstand.")), 0, 0);
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            backupFilter.Items.AddRange(new object[] { L("All changes", "Alle ændringer"), L("Layouts & macros", "Layouts og makroer"), L("Skins", "Skins"), L("Scripts", "Scripts") });
            filterRow.Controls.Add(WorkflowCombo(backupFilter, "guiBackupFilter"), 0, 0);
            WorkflowStatus(backupCount, "guiBackupCount"); backupCount.Margin = new Padding(12, 8, 0, 0); filterRow.Controls.Add(backupCount, 1, 0);
            var refresh = Actions(); refresh.Margin = Padding.Empty; filterRow.Controls.Add(refresh, 2, 0);
            ActionButton(refresh, L("Refresh", "Opdatér"), "refreshGuiBackups", () => { RefreshBackups(); return Task.CompletedTask; });
            root.Controls.Add(filterRow, 0, 1);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.RowStyles.Add(Fill());
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53)); root.Controls.Add(columns, 0, 2);
            var history = Card(); history.Margin = new Padding(0, 0, 12, 0); columns.Controls.Add(history, 0, 0);
            var list = Stack(Auto(), Fill()); history.Controls.Add(list); list.Controls.Add(SectionHeading(L("1  Choose a saved change", "1  Vælg en gemt ændring")), 0, 0);
            backups.Name = "guiBackups"; backups.Dock = DockStyle.Fill; backups.IntegralHeight = false; backups.BorderStyle = BorderStyle.None;
            backups.BackColor = Theme.Surface; backups.ForeColor = Theme.Foreground; backups.DrawMode = DrawMode.OwnerDrawFixed;
            Action sizeRows = () => backups.ItemHeight = TextRenderer.MeasureText("Ag", backups.Font).Height * 2 + 14;
            backups.FontChanged += (sender, e) => sizeRows(); sizeRows();
            backups.DrawItem += (sender, e) => {
                if (e.Index < 0 || e.Index >= backups.Items.Count) return;
                var entry = (BackupChoice)backups.Items[e.Index];
                using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                string kind = entry.Layout != null ? entry.Layout.Components == GuiLayoutComponents.Macros ? L("Macros", "Makroer") : L("Layout", "Layout") : entry.Script != null ? L("Scripts", "Scripts") : L("Skin", "Skin");
                int lineHeight = TextRenderer.MeasureText("Ag", backups.Font).Height;
                TextRenderer.DrawText(e.Graphics, kind + " · " + entry.When.ToLocalTime().ToString("g"), backups.Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 5, Math.Max(1, e.Bounds.Width - 16), lineHeight), Theme.Foreground, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, entry.Layout != null ? entry.Character.Label : entry.SkinTarget.ToString(), backups.Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y + lineHeight + 8, Math.Max(1, e.Bounds.Width - 16), lineHeight), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            backups.SelectedIndexChanged += (sender, e) => { if (!filteringBackups) UpdateSelection(); }; list.Controls.Add(SlimScrolling.Wrap(backups), 0, 1);
            var details = Card(); details.Margin = Padding.Empty; columns.Controls.Add(details, 1, 0);
            var detailStack = Stack(Auto(), Fill(), Auto(), Auto()); details.Controls.Add(detailStack);
            detailStack.Controls.Add(SectionHeading(L("2  Review what will be restored", "2  Gennemse det, der gendannes")), 0, 0);
            backupPreview.Name = "guiBackupPreview"; backupPreview.Dock = DockStyle.Fill; backupPreview.Multiline = true; backupPreview.ReadOnly = true;
            backupPreview.ScrollBars = ScrollBars.Vertical; backupPreview.BorderStyle = BorderStyle.None; backupPreview.BackColor = Theme.Surface; backupPreview.ForeColor = Theme.Foreground;
            detailStack.Controls.Add(SlimScrolling.Wrap(backupPreview), 0, 1);
            WorkflowStatus(backupInfo, "guiBackupInfo"); detailStack.Controls.Add(backupInfo, 0, 2);
            var action = Actions(); detailStack.Controls.Add(action, 0, 3);
            restoreBackup = ActionButton(action, L("Review restore…", "Gennemse gendannelse…"), "restoreGuiBackup", RestoreBackup, true);
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
            backupCount.Text = backups.Items.Count + L(" saved changes · newest first", " gemte ændringer · nyeste først");
            var choice = backups.SelectedItem as BackupChoice;
            if (choice == null) {
                backupInfo.Text = backupChoices.Count == 0 ? L("No backups yet. Your first change creates one automatically.", "Ingen sikkerhedskopier endnu. Din første ændring opretter en automatisk.") : L("No saved changes match this filter. Try All changes.", "Ingen gemte ændringer matcher filteret. Prøv Alle ændringer.");
                backupPreview.Text = L("Your recovery history will appear here.\r\n\r\nSelect a saved change to see its date, affected character or installation, and what Restore will change.", "Din gendannelseshistorik vises her.\r\n\r\nVælg en gemt ændring for at se dato, berørt char eller installation, og hvad Gendan vil ændre.");
                return;
            }
            var text = new StringBuilder();
            text.AppendLine(L("Saved before the change", "Gemt før ændringen")).AppendLine(choice.When.ToLocalTime().ToString("f")).AppendLine();
            string state = choice.Layout?.State ?? choice.Script?.State;
            if (state != null) text.AppendLine(BackupStateText(state)).AppendLine();
            else if (!choice.Skin.SelectionCompleted) text.AppendLine(L("The skin change did not finish. Review this recovery record before restoring.", "Skin-ændringen blev ikke afsluttet. Gennemse denne gendannelsespost før gendannelse.")).AppendLine();
            text.AppendLine(L("Restore to", "Gendan til"));
            if (choice.Layout != null) {
                text.AppendLine(choice.Character.Label).AppendLine().AppendLine(L("Settings restored", "Indstillinger, der gendannes"));
                text.AppendLine(GuiComponentChoices.Describe(choice.Layout.Components).Trim()).AppendLine();
                text.AppendLine(L("Other character settings are kept where possible. A conflicting later change can block restoration.", "Andre char-indstillinger bevares, hvor det er muligt. En modstridende senere ændring kan blokere gendannelse."));
                backupInfo.Text = L("Original character only. Nexus reviews any required relog before restoring.", "Kun den oprindelige char. Nexus gennemgår eventuelt genlogin før gendannelse.");
            } else if (choice.Script != null) {
                text.AppendLine(choice.SkinTarget.ToString()).AppendLine().AppendLine(L("Undo script change: ", "Fortryd script-ændring: ") + ScriptAction(choice.Script.Action));
                foreach (var change in choice.Script.Changes) text.AppendLine("• " + change.Name);
                backupInfo.Text = L("Current files are backed up first. Script changes need no relog.", "Aktuelle filer sikkerhedskopieres først. Script-ændringer kræver ikke genlogin.");
            } else {
                text.AppendLine(choice.SkinTarget.ToString()).AppendLine().AppendLine(L("Skin to reactivate", "Skin, der genaktiveres")).AppendLine(choice.Skin.SkinName).AppendLine();
                text.AppendLine(L("Changes the shared skin selection. Custom skin files and unrelated preferences are kept.", "Ændrer det fælles skin-valg. Egne skin-filer og øvrige indstillinger bevares."));
                backupInfo.Text = L("All accounts sharing this installation are affected. Nexus reviews any required relog.", "Alle konti på installationen berøres. Nexus gennemgår eventuelt genlogin.");
            }
            backupPreview.Text = text.ToString();
        }
        private static string BackupStateText(string state)
        {
            switch (state) {
                case "Applied": return L("Change completed · previous state saved", "Ændring udført · tidligere tilstand gemt");
                case "Restored": return L("This saved change has already been restored", "Denne gemte ændring er allerede gendannet");
                case "Rolled back": return L("The failed change was rolled back", "Den mislykkede ændring blev rullet tilbage");
                case "Recovery required": return L("Recovery needed · the earlier change could not be fully undone", "Gendannelse nødvendig · den tidligere ændring kunne ikke fortrydes helt");
                case "Applying": case "Restoring": return L("Incomplete operation · review this recovery record", "Uafsluttet handling · gennemse denne gendannelsespost");
                default: return L("Recorded status: ", "Registreret status: ") + state;
            }
        }
    }
}
