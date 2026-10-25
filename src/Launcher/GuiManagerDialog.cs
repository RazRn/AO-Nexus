using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class GuiCharacterChoice
    {
        public AccountConfig Account { get; set; }
        public CharacterConfig Character { get; set; }
        public string CharacterPath { get; set; }
        public string Label { get; set; }
        public override string ToString() => Label;
    }

    internal sealed class GuiManagerDialog : EditorDialog
    {
        private readonly LauncherSettings settings;
        private readonly string libraryRoot;
        private readonly bool preview;
        private readonly NexusTabControl tabs = new NexusTabControl();
        private readonly NexusTreeView targets = new NexusTreeView();
        private readonly ListBox templates = new ListBox(), skins = new ListBox(), backups = new ListBox();
        private readonly ComboBox skinTarget = new NexusComboBox(), catalog = new NexusComboBox(), clientEngine = new NexusComboBox();
        private readonly Label templateInfo = new Label(), selectedSummary = new Label(), skinInfo = new Label(), backupInfo = new Label();
        private readonly CheckBox defaultTemplate = new NexusCheckBox();
        private readonly List<GuiCharacterChoice> characters = new List<GuiCharacterChoice>();
        private readonly List<Button> selectionActions = new List<Button>();
        private readonly List<Button> skinActions = new List<Button>();
        private readonly Dictionary<GuiLayoutComponents, CheckBox> componentChecks = new Dictionary<GuiLayoutComponents, CheckBox>();
        private string componentTemplateId;
        private Button applyLayout, applyDefault, applySkin, restoreBackup;
        private bool updating, busy;
        private int unavailableSkinAccounts;
        private CancellationTokenSource downloadCancellation;
        public Func<string, List<GuiCharacterChoice>, bool, Action, Task<bool>> ExecuteOperation { get; set; }
        public Action RequestCancelOperation { get; set; }
        public Action SaveProfile { get; set; }
        public Action<string> SaveDefaultTemplate { get; set; }
        public string DefaultTemplateId { get; set; }
        internal GuiCharacterChoice CaptureSource { get; private set; }
        private string SkinBackupRoot => Path.Combine(libraryRoot, "SkinBackups");

        internal GuiManagerDialog(LauncherSettings settings, string libraryRoot, bool preview = false)
            : base(L("Game GUI", "Spillets GUI"), 700)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.libraryRoot = libraryRoot ?? throw new ArgumentNullException(nameof(libraryRoot)); this.preview = preview;
            DefaultTemplateId = settings.DefaultGuiTemplateId;
            ClientSize = new Size(1040, 700); MinimumSize = new Size(900, 620);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            DialogLayout.Padding = new Padding(18); DialogLayout.RowStyles[1].Height = 52;
            DialogLayout.Controls.Remove(FieldsViewport); FieldsViewport.Dispose();
            tabs.Name = "gameGuiTabs"; tabs.Dock = DockStyle.Fill; DialogLayout.Controls.Add(tabs, 0, 0);
            SaveButton.Visible = false; CancelAction.Text = L("Close", "Luk"); CancelAction.DialogResult = DialogResult.None;
            CancelAction.Click += (sender, e) => { if (busy) CancelOperation(); else Close(); };
            AcceptButton = null; CancelButton = CancelAction;
            ErrorLabel.ForeColor = Theme.Muted;
            foreach (var account in settings.Accounts)
                foreach (var character in account.Characters)
                {
                    string path = "";
                    try { if (!string.IsNullOrWhiteSpace(account.PrefsPath)) path = Path.GetFullPath(Path.Combine(account.PrefsPath, "Char" + character.CharacterId)); } catch (ArgumentException) { }
                    characters.Add(new GuiCharacterChoice { Account = account, Character = character, CharacterPath = path,
                        Label = character.Name + " — " + account.Label + " · " + AccountTargets.ServerName(settings, account) });
                }
            BuildLayouts(); BuildSkins(); BuildBackups();
            Shown += (sender, e) => RefreshLibrary();
            FormClosing += (sender, e) => { if (busy) { e.Cancel = true; CancelOperation(); } };
        }

        private TabPage Page(string title)
        {
            var page = new TabPage(title) { BackColor = Theme.Background, Padding = new Padding(12) };
            tabs.TabPages.Add(page); return page;
        }
        private static TableLayoutPanel Stack(params RowStyle[] rows)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows.Length, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foreach (var row in rows) panel.RowStyles.Add(row); return panel;
        }
        private static RowStyle Auto() => new RowStyle(SizeType.AutoSize);
        private static RowStyle Fill() => new RowStyle(SizeType.Percent, 100);
        private static Label Note(string text) => new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Muted, MaximumSize = new Size(800, 0), Margin = new Padding(0, 6, 0, 10) };
        private static FlowLayoutPanel Actions() => new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8) };
        private Button ActionButton(FlowLayoutPanel row, string text, string name, Func<Task> action, bool primary = false)
        {
            var button = Theme.Button(text, primary); button.Name = name;
            button.Click += async (sender, e) => await Safe(action); row.Controls.Add(button); return button;
        }
        private static void ListStyle(ListBox list, string name)
        {
            list.Name = name; list.Dock = DockStyle.Fill; list.IntegralHeight = false; list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface; list.ForeColor = Theme.Foreground; list.ItemHeight = 24; list.HorizontalScrollbar = true;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.DrawItem += (sender, e) =>
            {
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                using (var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Selection : Theme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(list.Items[e.Index]), list.Font, Rectangle.Inflate(e.Bounds, -4, 0), Theme.Foreground,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
        }
        private void BuildLayouts()
        {
            var page = Page(L("Layouts", "Layouts"));
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56)); page.Controls.Add(columns);
            var left = Stack(Auto(), Auto(), Fill(), Auto(), Auto(), Auto(), Auto()); left.Padding = new Padding(0, 0, 16, 0); columns.Controls.Add(left, 0, 0);
            left.Controls.Add(Note(L("LAYOUT TEMPLATES", "LAYOUTSKABELONER")), 0, 0);
            var actions = Actions(); left.Controls.Add(actions, 0, 1);
            ActionButton(actions, L("Capture from char…", "Gem fra char…"), "captureGuiLayout", () => CaptureLayout(false));
            ActionButton(actions, L("Copy from char…", "Kopiér fra char…"), "copyGuiLayout", () => CaptureLayout(true));
            ListStyle(templates, "guiTemplates"); templates.SelectedIndexChanged += (sender, e) => UpdateSelection(); left.Controls.Add(SlimScrolling.Wrap(templates), 0, 2);
            templateInfo.Name = "guiTemplateInfo"; templateInfo.Dock = DockStyle.Fill; templateInfo.Height = 78; templateInfo.ForeColor = Theme.Muted; templateInfo.Margin = new Padding(0, 10, 0, 4); left.Controls.Add(templateInfo, 0, 3);
            defaultTemplate.Name = "defaultGuiTemplate"; defaultTemplate.Text = L("Use for new chars", "Brug til nye chars"); defaultTemplate.AutoSize = false; defaultTemplate.Height = 28; defaultTemplate.Dock = DockStyle.Top;
            defaultTemplate.CheckedChanged += (sender, e) =>
            {
                if (updating || preview || busy || !(templates.SelectedItem is TemplateChoice choice)) return;
                try
                {
                    string value = defaultTemplate.Checked ? choice.Value.Id : null;
                    SaveDefaultTemplate?.Invoke(value); DefaultTemplateId = value;
                    Status(L("Default changed for future chars. Existing layouts are unchanged.", "Standard ændret for fremtidige chars. Eksisterende layouts er uændrede."));
                }
                catch (Exception error) { Status(error.Message, true); }
                UpdateSelection();
            };
            var components = GuiComponentChoices.Create(componentChecks, "applyGuiComponent", () => { if (!updating) UpdateSelection(); }); left.Controls.Add(components, 0, 4);
            left.Controls.Add(defaultTemplate, 0, 5);
            var manage = Actions(); left.Controls.Add(manage, 0, 6);
            selectionActions.Add(ActionButton(manage, L("Rename…", "Omdøb…"), "renameGuiTemplate", RenameTemplate));
            selectionActions.Add(ActionButton(manage, L("Delete", "Slet"), "deleteGuiTemplate", DeleteTemplate));

            var right = Stack(Auto(), Fill(), Auto(), Auto()); columns.Controls.Add(right, 1, 0);
            var header = Actions(); header.Controls.Add(Note(L("TARGET CHARACTERS", "MÅL-CHARS")));
            var select = new LinkLabel { Text = L("Select all", "Vælg alle"), AutoSize = true, Margin = new Padding(12, 6, 6, 6), DisabledLinkColor = Theme.Muted };
            var clear = new LinkLabel { Text = L("Clear", "Fravælg"), AutoSize = true, Margin = new Padding(6), DisabledLinkColor = Theme.Muted };
            select.Click += (sender, e) => { if (!busy) SetAllTargets(true); }; clear.Click += (sender, e) => { if (!busy) SetAllTargets(false); }; header.Controls.Add(select); header.Controls.Add(clear); right.Controls.Add(header, 0, 0);
            targets.Name = "guiTargets"; targets.Dock = DockStyle.Fill; targets.CheckBoxes = true; targets.BorderStyle = BorderStyle.None; targets.BackColor = Theme.Surface;
            targets.ForeColor = Theme.Foreground; targets.ItemHeight = 28; targets.HideSelection = false; targets.ShowNodeToolTips = false;
            foreach (var server in characters.GroupBy(choice => AccountTargets.ServerName(settings, choice.Account)).OrderBy(group => group.Key))
            {
                var serverNode = new TreeNode(server.Key) { ForeColor = Theme.Accent };
                foreach (var account in server.GroupBy(choice => choice.Account.Id))
                {
                    var accountNode = new TreeNode(account.First().Account.Label);
                    foreach (var choice in account) accountNode.Nodes.Add(new TreeNode(choice.Character.Name + (string.IsNullOrEmpty(choice.Character.PendingGuiTemplateId) ? "" : L(" · default pending", " · afventer standard"))) { Tag = choice });
                    serverNode.Nodes.Add(accountNode);
                }
                targets.Nodes.Add(serverNode);
            }
            targets.ExpandAll(); targets.AfterCheck += (sender, e) =>
            {
                if (updating) return; updating = true;
                try { foreach (var node in Descendants(e.Node)) node.Checked = e.Node.Checked; for (var parent = e.Node.Parent; parent != null; parent = parent.Parent) parent.Checked = parent.Nodes.Cast<TreeNode>().All(node => node.Checked); }
                finally { updating = false; }
                UpdateSelection();
            };
            right.Controls.Add(SlimScrolling.Wrap(targets), 0, 1);
            selectedSummary.Name = "guiTargetSummary"; selectedSummary.Dock = DockStyle.Fill; selectedSummary.Height = 60; selectedSummary.ForeColor = Theme.Muted; selectedSummary.Margin = new Padding(0, 8, 0, 2); right.Controls.Add(selectedSummary, 0, 2);
            var apply = Actions(); right.Controls.Add(apply, 0, 3);
            applyLayout = ActionButton(apply, L("Apply layout", "Anvend layout"), "applyGuiLayout", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id), true);
            applyDefault = ActionButton(apply, L("Apply saved default", "Anvend gemt standard"), "applyDefaultGui", () => ApplyTemplate(DefaultTemplateId));
            ActionButton(apply, L("Roll out to all…", "Udrul til alle…"), "rolloutAllGuiLayouts", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id, true));
        }
        private static IEnumerable<TreeNode> Descendants(TreeNode parent) { foreach (TreeNode child in parent.Nodes) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        private IEnumerable<TreeNode> Leaves() => targets.Nodes.Cast<TreeNode>().SelectMany(node => Descendants(node)).Where(node => node.Tag is GuiCharacterChoice);
        internal List<GuiCharacterChoice> SelectedCharacters() => Leaves().Where(node => node.Checked).Select(node => (GuiCharacterChoice)node.Tag).ToList();
        private void SetAllTargets(bool value) { updating = true; foreach (TreeNode node in targets.Nodes) { node.Checked = value; foreach (var child in Descendants(node)) child.Checked = value; } updating = false; UpdateSelection(); }

        private void BuildSkins()
        {
            var page = Page(L("Skins", "Skins")); var layout = Stack(Auto(), Auto(), Auto(), Fill(), Auto(), Auto()); page.Controls.Add(layout);
            layout.Controls.Add(Note(L("Skins are shared by accounts using the same Prefs installation. Download/import adds a skin; Apply activates it after clients close. Select the matching classic/new-engine package. Try a skin before rolling it out to every installation.", "Skins deles af konti med samme Prefs-installation. Download/import tilføjer et skin; Anvend aktiverer det, når klienterne er lukket. Vælg den rigtige klassiske/nye grafikmotor. Prøv et skin, før du udruller det til alle installationer.")), 0, 0);
            skinTarget.Name = "guiSkinTarget"; skinTarget.DropDownStyle = ComboBoxStyle.DropDownList; skinTarget.Dock = DockStyle.Top;
            foreach (var account in settings.Accounts)
            {
                try
                {
                    string root = GuiSkins.ResolvePrefsRoot(account.PrefsPath);
                    var existing = skinTarget.Items.Cast<SkinTargetChoice>().FirstOrDefault(item => string.Equals(item.Root, root, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) { existing = new SkinTargetChoice { Root = root, Name = Path.GetFileName(Path.GetDirectoryName(root)) }; skinTarget.Items.Add(existing); }
                    var owned = characters.Where(choice => choice.Account.Id == account.Id).ToList();
                    if (owned.Count == 0) owned.Add(new GuiCharacterChoice { Account = account, CharacterPath = "", Label = account.Label });
                    existing.Characters.AddRange(owned);
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException) { unavailableSkinAccounts++; }
            }
            skinTarget.SelectedIndexChanged += (sender, e) => RefreshSkins(); layout.Controls.Add(skinTarget, 0, 1);
            var downloads = Actions(); layout.Controls.Add(downloads, 0, 2);
            catalog.Name = "guiSkinCatalog"; catalog.DropDownStyle = ComboBoxStyle.DropDownList; catalog.Width = 235;
            foreach (var entry in GuiSkins.Catalog) catalog.Items.Add(new CatalogChoice { Value = entry }); if (catalog.Items.Count > 0) catalog.SelectedIndex = 0;
            clientEngine.Name = "guiSkinEngine"; clientEngine.DropDownStyle = ComboBoxStyle.DropDownList; clientEngine.Width = 150;
            clientEngine.Items.AddRange(new object[] { L("Classic client", "Klassisk klient"), L("New engine", "Ny grafikmotor") }); clientEngine.SelectedIndex = 0;
            downloads.Controls.Add(catalog); downloads.Controls.Add(clientEngine);
            var download = ActionButton(downloads, L("Download", "Download"), "downloadGuiSkin", DownloadSkin);
            skinActions.Add(download);
            skinActions.Add(ActionButton(downloads, L("Import ZIP…", "Importér ZIP…"), "importGuiSkin", ImportSkin));
            if (catalog is NexusComboBox catalogBox) catalogBox.MatchHeight(download); if (clientEngine is NexusComboBox engineBox) engineBox.MatchHeight(download); if (skinTarget is NexusComboBox targetBox) targetBox.MatchHeight(download);
            ListStyle(skins, "guiInstalledSkins"); skins.SelectedIndexChanged += (sender, e) => UpdateSelection(); layout.Controls.Add(SlimScrolling.Wrap(skins), 0, 3);
            skinInfo.Name = "guiSkinInfo"; skinInfo.Dock = DockStyle.Fill; skinInfo.Height = 88; skinInfo.ForeColor = Theme.Muted; skinInfo.Margin = new Padding(0, 10, 0, 4); layout.Controls.Add(skinInfo, 0, 4);
            var apply = Actions(); layout.Controls.Add(apply, 0, 5); applySkin = ActionButton(apply, L("Apply skin", "Anvend skin"), "applyGuiSkin", ApplySkin, true);
            skinActions.Add(ActionButton(apply, L("Use default AO skin", "Brug AO-standardskin"), "defaultGuiSkin", () => ApplySkinName("Default")));
            skinActions.Add(ActionButton(apply, L("Roll out to all…", "Udrul til alle…"), "rolloutAllGuiSkins", RolloutSkin));
            ActionButton(apply, L("Skin website ↗", "Skin-hjemmeside ↗"), "guiSkinWebsite", () => { if (catalog.SelectedItem is CatalogChoice entry && !preview) Process.Start(new ProcessStartInfo(entry.Value.SourceUrl) { UseShellExecute = true }); return Task.CompletedTask; });
            if (skinTarget.Items.Count > 0) skinTarget.SelectedIndex = 0;
        }
        private void BuildBackups()
        {
            var page = Page(L("Backups", "Sikkerhedskopier")); var layout = Stack(Auto(), Fill(), Auto(), Auto()); page.Controls.Add(layout);
            layout.Controls.Add(Note(L("A backup is created before each layout or skin selection change. Restore affects only the original target and asks about relogging when needed.", "Der laves en sikkerhedskopi før hver ændring af layout eller skin-valg. Gendan ændrer kun det oprindelige mål og spørger om genlogin efter behov.")), 0, 0);
            ListStyle(backups, "guiBackups"); backups.SelectedIndexChanged += (sender, e) => UpdateSelection(); layout.Controls.Add(SlimScrolling.Wrap(backups), 0, 1);
            backupInfo.Name = "guiBackupInfo"; backupInfo.Dock = DockStyle.Fill; backupInfo.Height = 70; backupInfo.ForeColor = Theme.Muted; backupInfo.Margin = new Padding(0, 10, 0, 4); layout.Controls.Add(backupInfo, 0, 2);
            var actions = Actions(); layout.Controls.Add(actions, 0, 3); restoreBackup = ActionButton(actions, L("Restore backup", "Gendan sikkerhedskopi"), "restoreGuiBackup", RestoreBackup, true);
            ActionButton(actions, L("Refresh list", "Opdater liste"), "refreshGuiBackups", () => { RefreshBackups(); return Task.CompletedTask; });
        }

        private async Task Safe(Func<Task> action)
        {
            if (busy) return;
            try { if (preview) { Status(L("Preview: no game files are changed.", "Forhåndsvisning: spilfiler ændres ikke.")); return; } await action(); }
            catch (Exception error) { Status(error.Message, true); }
        }
        private void SetBusy(bool value)
        {
            busy = value;
            // Disabling a container forces native disabled-label colours over the dark theme.
            // Keep headings/help readable and lock only controls that can change the operation.
            foreach (var control in ChildControls(tabs))
            {
                if (control is LinkLabel link)
                {
                    link.LinkColor = link.VisitedLinkColor = link.ActiveLinkColor = value ? Theme.Muted : Theme.Accent;
                    link.LinkBehavior = value ? LinkBehavior.NeverUnderline : LinkBehavior.SystemDefault;
                    link.TabStop = !value;
                }
                else if (control is ButtonBase || control is ComboBox || control is ListBox || control is TreeView) control.Enabled = !value;
            }
            CancelAction.Text = value ? L("Cancel operation", "Annuller handling") : L("Close", "Luk");
            UpdateSelection();
        }
        private static IEnumerable<Control> ChildControls(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in ChildControls(child)) yield return descendant; }
        }
        private void CancelOperation() { downloadCancellation?.Cancel(); RequestCancelOperation?.Invoke(); Status(L("Cancellation requested. Waiting for the current safe step.", "Annullering anmodet. Afventer det aktuelle sikre trin.")); }
        private void Status(string message, bool error = false) { ErrorLabel.Text = message; ErrorLabel.ForeColor = error ? Theme.Warning : Theme.Muted; }
        private async Task<bool> Run(string description, List<GuiCharacterChoice> choices, bool installationWide, Action action)
        {
            if (ExecuteOperation == null) { Status(L("This operation is unavailable in preview.", "Handlingen er ikke tilgængelig i forhåndsvisning.")); return false; }
            SetBusy(true); Status(description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0]);
            try { bool result = await ExecuteOperation(description, choices, installationWide, action); if (result) Status(L("Operation completed.", "Handlingen er udført.")); return result; }
            finally { SetBusy(false); }
        }
        private async Task CaptureLayout(bool copy)
        {
            var selected = UniqueTargets(SelectedCharacters()); if (copy && selected.Count == 0) { Status(L("Select target chars first.", "Vælg mål-chars først.")); return; }
            using (var dialog = new GuiCaptureDialog(characters, copy))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var source = dialog.Source; if (source == null) return;
                string name = dialog.TemplateName; var components = dialog.Components;
                selected = selected.Where(choice => !string.Equals(choice.CharacterPath, source.CharacterPath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (copy && selected.Count == 0) { Status(L("Select at least one target other than the source char.", "Vælg mindst ét mål ud over kilde-char.")); return; }
                string signature = ""; try { signature = FileVersionInfo.GetVersionInfo(Path.Combine(source.Account.GamePath, "AnarchyOnline.exe")).FileVersion ?? ""; } catch { }
                GuiLayoutTemplate captured = null;
                var involved = new List<GuiCharacterChoice> { source }; if (copy) involved.AddRange(selected.Where(choice => choice != source));
                bool success;
                CaptureSource = source;
                try
                {
                    success = await Run((copy ? L("Copy character layout", "Kopiér char-layout") : L("Capture character layout", "Gem char-layout")) + GuiComponentChoices.Describe(components), involved, false, () =>
                    {
                        captured = GuiLayouts.CaptureWithOptions(libraryRoot, name, source.CharacterPath, source.Character.Name, signature, components);
                        if (copy) try { ApplyLayoutBatch(captured.Id, selected, components); }
                            finally { GuiLayouts.DeleteTemplate(libraryRoot, captured.Id); }
                    });
                }
                finally { CaptureSource = null; }
                if (success && copy) { foreach (var target in selected) target.Character.PendingGuiTemplateId = null; SaveProfile?.Invoke(); }
                if (success && !copy && dialog.UseForNewCharacters) { SaveDefaultTemplate?.Invoke(captured.Id); DefaultTemplateId = captured.Id; }
                RefreshLibrary(captured?.Id);
            }
        }
        private async Task ApplyTemplate(string id, bool all = false)
        {
            var selected = UniqueTargets(all ? characters.ToList() : SelectedCharacters()); if (string.IsNullOrEmpty(id) || selected.Count == 0) return;
            var template = id == GuiLayouts.GameDefaultId ? GameDefaultTemplate() : GuiLayouts.ListTemplates(libraryRoot).FirstOrDefault(item => item.Id == id); if (template == null) return;
            var components = (templates.SelectedItem as TemplateChoice)?.Value.Id == id ? GuiComponentChoices.Selected(componentChecks) : template.Components;
            if (components == 0) { Status(L("Choose at least one component to apply.", "Vælg mindst én del at anvende.")); return; }
            string description = id == GuiLayouts.GameDefaultId
                ? L("Reset supported window layout using each character's game installation defaults. Hotbar actions/count, key bindings, pet names, chat channels and the current skin are kept. Previous layouts are backed up.", "Nulstil understøttet vindueslayout med standarder fra hver chars spilinstallation. Hotbar-handlinger/antal, taster, kæledyrsnavne, chatkanaler og aktuelt skin bevares. Tidligere layouts sikkerhedskopieres.")
                : (all ? L("Roll out GUI template to all chars", "Udrul GUI-skabelon til alle chars") : L("Apply GUI template to selected chars", "Anvend GUI-skabelon på valgte chars")) + GuiComponentChoices.Describe(components);
            if (await Run(description, selected, false,
                () => ApplyLayoutBatch(id, selected, components)))
            { foreach (var target in selected) target.Character.PendingGuiTemplateId = null; SaveProfile?.Invoke(); }
            RefreshBackups();
        }
        private void ApplyLayoutBatch(string id, List<GuiCharacterChoice> selected, GuiLayoutComponents components)
        {
            selected = UniqueTargets(selected);
            foreach (var target in selected)
                if (id == GuiLayouts.GameDefaultId) GuiLayouts.ResetToGameDefault(libraryRoot, target.Account.GamePath, target.CharacterPath, true);
                else GuiLayouts.PreflightApplyWithOptions(libraryRoot, id, target.CharacterPath, components);
            var applied = new List<GuiLayoutBackup>();
            try { foreach (var target in selected) { var backup = id == GuiLayouts.GameDefaultId ? GuiLayouts.ResetToGameDefault(libraryRoot, target.Account.GamePath, target.CharacterPath, false) : GuiLayouts.ApplyWithOptions(libraryRoot, id, target.CharacterPath, components); if (!string.IsNullOrEmpty(backup.Id)) applied.Add(backup); } }
            catch (Exception failure)
            {
                var errors = new List<string>();
                foreach (var backup in applied.AsEnumerable().Reverse()) try { GuiLayouts.Restore(libraryRoot, backup.Id, backup.TargetPath); } catch (Exception rollback) { errors.Add(backup.TargetPath + ": " + rollback.Message); }
                throw new IOException(failure.Message + "\r\n" + (errors.Count == 0 ? L("Earlier targets in this batch were restored.", "Tidligere mål i denne gruppe blev gendannet.")
                    : L("Some targets need manual backup restoration:\r\n", "Nogle mål kræver manuel gendannelse fra sikkerhedskopi:\r\n") + string.Join("\r\n", errors)), failure);
            }
        }
        private static List<GuiCharacterChoice> UniqueTargets(IEnumerable<GuiCharacterChoice> targets)
            => targets.GroupBy(choice => choice.CharacterPath ?? "", StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        private Task RenameTemplate()
        {
            if (!(templates.SelectedItem is TemplateChoice choice)) return Task.CompletedTask;
            using (var dialog = new GuiTemplateNameDialog(choice.Value.Name))
                if (dialog.ShowDialog(this) == DialogResult.OK) { GuiLayouts.RenameTemplate(libraryRoot, choice.Value.Id, dialog.Value); RefreshLibrary(choice.Value.Id); }
            return Task.CompletedTask;
        }
        private Task DeleteTemplate()
        {
            if (!(templates.SelectedItem is TemplateChoice choice)) return Task.CompletedTask;
            if (NexusMessageBox.Show(this, L("Delete this saved template? Existing game layouts and backups are kept.", "Slet denne gemte skabelon? Eksisterende spillayouts og sikkerhedskopier bevares."), choice.Value.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK)
            {
                GuiLayouts.DeleteTemplate(libraryRoot, choice.Value.Id);
                if (DefaultTemplateId == choice.Value.Id) { SaveDefaultTemplate?.Invoke(null); DefaultTemplateId = null; }
                foreach (var target in characters.Where(item => item.Character.PendingGuiTemplateId == choice.Value.Id)) target.Character.PendingGuiTemplateId = null;
                SaveProfile?.Invoke(); RefreshLibrary();
            }
            return Task.CompletedTask;
        }
        private async Task DownloadSkin()
        {
            if (!(skinTarget.SelectedItem is SkinTargetChoice target) || !(catalog.SelectedItem is CatalogChoice source)) return;
            SetBusy(true); downloadCancellation = new CancellationTokenSource();
            try { Status(L("Downloading skin from its author…", "Henter skin fra forfatteren…")); var result = await GuiSkins.InstallNdGuiAsync(target.Root, source.Value.Id, clientEngine.SelectedIndex == 1, downloadCancellation.Token); RefreshSkins(result.SkinName); Status(L("Skin installed. Choose Apply skin to activate it.", "Skin installeret. Vælg Anvend skin for at aktivere det.")); }
            finally { downloadCancellation.Dispose(); downloadCancellation = null; SetBusy(false); }
        }
        private async Task ImportSkin()
        {
            if (!(skinTarget.SelectedItem is SkinTargetChoice target)) return;
            using (var picker = new OpenFileDialog { Filter = L("Skin ZIP files (*.zip)|*.zip", "Skin ZIP-filer (*.zip)|*.zip"), CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return; SetBusy(true);
                try { var result = await Task.Run(() => GuiSkins.ImportZip(target.Root, picker.FileName)); RefreshSkins(result.SkinName); Status(L("Skin imported. Choose Apply skin to activate it.", "Skin importeret. Vælg Anvend skin for at aktivere det.")); }
                finally { SetBusy(false); }
            }
        }
        private Task ApplySkin() => ApplySkinName(skins.SelectedItem as string);
        private async Task RolloutSkin()
        {
            if (!(skinTarget.SelectedItem is SkinTargetChoice source) || !(skins.SelectedItem is string name)) return;
            var destinations = skinTarget.Items.Cast<SkinTargetChoice>().Where(item => !string.Equals(GuiSkins.CurrentSkin(item.Root), name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (destinations.Count == 0) { Status(L("All installations already use this skin.", "Alle installationer bruger allerede dette skin.")); return; }
            await Run(L("Roll out skin to all configured Prefs installations. The same skin variant is copied; verify Classic/new-engine compatibility for each installation.", "Udrul skin til alle konfigurerede Prefs-installationer. Samme skin-variant kopieres; kontrollér klassisk/ny grafikmotor-kompatibilitet for hver installation."), destinations.SelectMany(item => item.Characters).ToList(), true, () =>
            {
                var applied = new List<Tuple<string, string>>();
                try
                {
                    foreach (var target in destinations)
                    {
                        string targetName = name;
                        if (name != "Default" && !string.Equals(source.Root, target.Root, StringComparison.OrdinalIgnoreCase)) targetName = GuiSkins.CopyInstalledSkin(source.Root, name, target.Root).SkinName;
                        applied.Add(Tuple.Create(target.Root, GuiSkins.SelectSkin(target.Root, targetName, SkinBackupRoot)));
                    }
                }
                catch (Exception failure)
                {
                    var errors = new List<string>();
                    foreach (var item in applied.AsEnumerable().Reverse()) try { GuiSkins.RestoreSelection(item.Item1, item.Item2); } catch (Exception rollback) { errors.Add(item.Item1 + ": " + rollback.Message); }
                    throw new IOException(failure.Message + "\r\n" + (errors.Count == 0 ? L("Earlier skin selections were restored.", "Tidligere skin-valg blev gendannet.")
                        : L("Some installations need manual backup restoration:\r\n", "Nogle installationer kræver manuel gendannelse fra sikkerhedskopi:\r\n") + string.Join("\r\n", errors)), failure);
                }
            });
            RefreshSkins(); RefreshBackups();
        }
        private async Task ApplySkinName(string name)
        {
            if (!(skinTarget.SelectedItem is SkinTargetChoice target) || string.IsNullOrEmpty(name)) return;
            if (string.Equals(GuiSkins.CurrentSkin(target.Root), name, StringComparison.OrdinalIgnoreCase)) { Status(L("This skin is already selected.", "Dette skin er allerede valgt.")); return; }
            await Run(L("Change GUI skin", "Skift GUI-skin"), target.Characters.ToList(), true, () => GuiSkins.SelectSkin(target.Root, name, SkinBackupRoot));
            RefreshSkins(name); RefreshBackups();
        }
        private async Task RestoreBackup()
        {
            if (!(backups.SelectedItem is BackupChoice choice)) return;
            if (choice.Layout != null)
                await Run(L("Restore character GUI backup", "Gendan char-GUI"), new List<GuiCharacterChoice> { choice.Character }, false, () => GuiLayouts.Restore(libraryRoot, choice.Layout.Id, choice.Character.CharacterPath));
            else await Run(L("Restore skin selection", "Gendan skin-valg"), choice.SkinTarget.Characters.ToList(), true, () => GuiSkins.RestoreSelection(choice.SkinTarget.Root, choice.Skin.Directory));
            RefreshSkins(); RefreshBackups();
        }
        internal void RefreshLibrary(string selectedId = null)
        {
            try
            {
                selectedId = selectedId ?? (templates.SelectedItem as TemplateChoice)?.Value.Id;
                templates.Items.Clear(); templates.Items.Add(new TemplateChoice { Value = GameDefaultTemplate() });
                foreach (var item in GuiLayouts.ListTemplates(libraryRoot)) templates.Items.Add(new TemplateChoice { Value = item });
                templates.SelectedItem = templates.Items.Cast<TemplateChoice>().FirstOrDefault(item => item.Value.Id == selectedId);
                if (templates.SelectedIndex < 0 && templates.Items.Count > 0) templates.SelectedIndex = 0;
                RefreshSkins(); RefreshBackups(); UpdateSelection();
                if (preview) Status(L("Preview uses example accounts. No game files will be changed.", "Forhåndsvisning bruger eksempelkontoer. Spilfiler ændres ikke."));
            }
            catch (Exception error) { Status(error.Message, true); }
        }
        private void RefreshSkins(string selectedName = null)
        {
            try
            {
                skins.Items.Clear();
                if (skinTarget.SelectedItem is SkinTargetChoice target)
                {
                    foreach (string name in GuiSkins.InstalledSkins(target.Root)) skins.Items.Add(name);
                    skins.SelectedItem = selectedName ?? GuiSkins.CurrentSkin(target.Root);
                    if (skins.SelectedIndex < 0 && skins.Items.Count > 0) skins.SelectedIndex = 0;
                    skinInfo.Text = L("Current skin: ", "Aktuelt skin: ") + GuiSkins.CurrentSkin(target.Root) + "\r\n" + target.Root;
                    if (unavailableSkinAccounts > 0) skinInfo.Text += "\r\n" + unavailableSkinAccounts + L(" account(s) need an initialized Prefs folder. Choose it under Edit account.", " konti mangler en initialiseret Prefs-mappe. Vælg den under Rediger konto.");
                }
                else skinInfo.Text = L("Choose an account Prefs folder first in Edit account. No valid Prefs installation is available.", "Vælg først kontoens Prefs-mappe under Rediger konto. Ingen gyldig Prefs-installation er tilgængelig.");
            }
            catch (Exception error) { skinInfo.Text = error.Message; }
            UpdateSelection();
        }
        private void RefreshBackups()
        {
            backups.Items.Clear(); var entries = new List<BackupChoice>();
            foreach (var choice in characters.Where(item => !string.IsNullOrEmpty(item.CharacterPath) && Directory.Exists(item.CharacterPath)).GroupBy(item => item.CharacterPath, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
                foreach (var backup in GuiLayouts.ListBackups(libraryRoot, choice.CharacterPath)) entries.Add(new BackupChoice { Layout = backup, Character = choice, When = backup.CreatedUtc });
            foreach (var target in skinTarget.Items.Cast<SkinTargetChoice>())
                foreach (var backup in GuiSkins.ListBackups(target.Root, SkinBackupRoot)) entries.Add(new BackupChoice { Skin = backup, SkinTarget = target, When = backup.CreatedUtc });
            foreach (var entry in entries.OrderByDescending(entry => entry.When).Take(200)) backups.Items.Add(entry);
            if (backups.Items.Count > 0) backups.SelectedIndex = 0; UpdateSelection();
        }
        private void UpdateSelection()
        {
            if (applyLayout == null || applyDefault == null) return;
            var selected = SelectedCharacters(); var template = (templates.SelectedItem as TemplateChoice)?.Value;
            foreach (var leaf in Leaves())
            {
                var choice = (GuiCharacterChoice)leaf.Tag;
                string suffix = !string.IsNullOrEmpty(choice.Character.PendingGuiTemplateId) ? L(" · default pending", " · afventer standard") : "";
                if (string.IsNullOrEmpty(choice.CharacterPath) || !Directory.Exists(choice.CharacterPath)) suffix += L(" · Prefs missing", " · Prefs mangler");
                string text = choice.Character.Name + suffix;
                if (leaf.Text != text) leaf.Text = text;
            }
            bool builtIn = template?.Id == GuiLayouts.GameDefaultId;
            templateInfo.Text = builtIn ? L("Built in · Uses the target's game defaults. Resets supported window positions and visibility. Keeps hotbar actions/count, bindings, pets, chat channels and skin.", "Indbygget · Bruger målets spilstandarder. Nulstiller understøttede vinduesplaceringer og synlighed. Bevarer hotbar-handlinger/antal, taster, kæledyr, chatkanaler og skin.") : template == null ? L("Capture a character layout to create your first template.", "Gem et char-layout for at oprette din første skabelon.")
                : template.Name + "\r\n" + L("Source: ", "Kilde: ") + template.SourceCharacter + "\r\n" + template.CreatedUtc.ToLocalTime().ToString("g") + " · " + template.FileCount + (template.FileCount == 1 ? L(" file", " fil") : L(" files", " filer"));
            bool previousUpdating = updating; updating = true;
            foreach (var pair in componentChecks)
            {
                pair.Value.Enabled = !busy && template != null && (template.Components & pair.Key) != 0;
                if (componentTemplateId != template?.Id) pair.Value.Checked = template != null && (template.Components & pair.Key) != 0;
            }
            componentTemplateId = template?.Id; defaultTemplate.Checked = template != null && template.Id == DefaultTemplateId; updating = previousUpdating;
            defaultTemplate.Enabled = !busy && !preview && template != null && !builtIn;
            selectedSummary.Text = selected.Count + L(" chars selected. Login selections and team presets are unchanged.", " chars valgt. Loginvalg og hold er uændrede.")
                + "\r\n" + (selected.Any(choice => string.IsNullOrEmpty(choice.CharacterPath) || !Directory.Exists(choice.CharacterPath))
                    ? L("Missing Prefs: log in once, then choose the account folder in Edit account.", "Manglende Prefs: log ind én gang, og vælg derefter kontomappen under Rediger konto.")
                    : L("Running clients are handled before any layout is replaced.", "Aktive klienter håndteres, før et layout erstattes."));
            applyLayout.Enabled = !busy && template != null && selected.Count > 0; applyDefault.Enabled = !busy && !string.IsNullOrEmpty(DefaultTemplateId) && selected.Count > 0;
            foreach (var button in selectionActions) button.Enabled = !busy && template != null && !builtIn;
            if (applySkin != null) applySkin.Enabled = !busy && skinTarget.SelectedItem != null && skins.SelectedItem != null;
            foreach (var button in skinActions) button.Enabled = !busy && skinTarget.SelectedItem != null;
            if (restoreBackup != null) restoreBackup.Enabled = !busy && backups.SelectedItem != null;
            var backup = backups.SelectedItem as BackupChoice;
            backupInfo.Text = backup == null ? L("Backups appear after your first change.", "Sikkerhedskopier vises efter din første ændring.")
                : backup.Layout != null ? backup.Character.Label + "\r\n" + backup.Layout.TargetPath + "\r\n" + backup.Layout.State : backup.SkinTarget.Root + "\r\n" + L("Previous skin: ", "Tidligere skin: ") + backup.Skin.SkinName;
        }
        private static GuiLayoutTemplate GameDefaultTemplate() => new GuiLayoutTemplate { Id = GuiLayouts.GameDefaultId, Name = L("AO default layout", "AO-standardlayout"), Components = GuiLayoutComponents.Layout };
        private sealed class TemplateChoice { internal GuiLayoutTemplate Value; public override string ToString() => Value.Name; }
        private sealed class CatalogChoice { internal GuiSkinCatalogEntry Value; public override string ToString() => Value.Name; }
        private sealed class SkinTargetChoice
        {
            internal string Root, Name; internal readonly List<GuiCharacterChoice> Characters = new List<GuiCharacterChoice>();
            public override string ToString() => Name + " — " + string.Join(", ", Characters.Select(choice => choice.Account.Label).Distinct());
        }
        private sealed class BackupChoice
        {
            internal GuiLayoutBackup Layout; internal GuiSkinBackup Skin; internal GuiCharacterChoice Character; internal SkinTargetChoice SkinTarget; internal DateTime When;
            public override string ToString() => When.ToLocalTime().ToString("g") + " · " + (Layout != null ? L("Layout — ", "Layout — ") + Character.Label : L("Skin — ", "Skin — ") + SkinTarget.Name);
        }
    }

    internal sealed class GuiCaptureDialog : EditorDialog
    {
        private readonly ComboBox source;
        private readonly TextBox name;
        private readonly CheckBox useDefault;
        private readonly Dictionary<GuiLayoutComponents, CheckBox> components = new Dictionary<GuiLayoutComponents, CheckBox>();
        internal GuiCharacterChoice Source => source.SelectedItem as GuiCharacterChoice;
        internal string TemplateName => name.Text.Trim();
        internal bool UseForNewCharacters => useDefault.Checked;
        internal GuiLayoutComponents Components => GuiComponentChoices.Selected(components);
        internal GuiCaptureDialog(IEnumerable<GuiCharacterChoice> characters, bool copy) : base(copy ? L("Copy layout from char", "Kopiér layout fra char") : L("Capture GUI layout", "Gem GUI-layout"), 520)
        {
            ClientSize = new Size(620, 600);
            AddWrappedNote(L("AO saves layout changes on normal logout. A running source must /quit before capture or copy; Nexus can relog it for you. If it is already closed, confirm that its latest changes were saved by a normal logout.", "AO gemmer layoutændringer ved normalt logout. En aktiv kilde skal afslutte med /quit før kopiering; Nexus kan genlogge den for dig. Er den allerede lukket, skal du bekræfte, at de seneste ændringer blev gemt ved normalt logout."), "captureGuiGuide");
            source = new NexusComboBox { Name = "guiCaptureSource", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var character in characters) source.Items.Add(character); Fields.Controls.Add(source);
            name = AddTextField(L("Template name", "Skabelonnavn"), copy ? "Character copy" : ""); name.MaxLength = 80;
            if (copy) name.Parent.Visible = false;
            Fields.Controls.Add(GuiComponentChoices.Create(components, "captureGuiComponent", null));
            components[GuiLayoutComponents.Layout].Checked = true;
            AddWrappedNote(L("Hotbars can refer to nanos, items or macros that another char does not have. Include hotbars, keys and pet names only when you want to replace them.", "Hotbars kan henvise til nanoer, genstande eller makroer, som en anden char ikke har. Medtag kun hotbars, taster og kæledyrsnavne, når du vil erstatte dem."), "captureGuiComponentsGuide");
            useDefault = new NexusCheckBox { Name = "newGuiTemplateDefault", Text = L("Use for new chars", "Brug til nye chars"), AutoSize = false, Height = 28, Dock = DockStyle.Top, Visible = !copy };
            Fields.Controls.Add(useDefault);
            AddWrappedNote(L("Only supported interface settings are copied. Existing characters are changed only when you select them as targets.", "Kun understøttede interfaceindstillinger kopieres. Eksisterende chars ændres kun, når du vælger dem som mål."), "captureGuiScope");
            SaveButton.Text = copy ? L("Copy to selected", "Kopiér til valgte") : L("Capture", "Gem layout");
            SaveButton.Click += (sender, e) =>
            {
                if (Source == null || string.IsNullOrWhiteSpace(TemplateName) || Components == 0) { ErrorLabel.Text = L("Choose a source, template name and at least one component.", "Vælg kilde, skabelonnavn og mindst én del."); return; }
                DialogResult = DialogResult.OK;
            };
        }
    }
    internal static class GuiComponentChoices
    {
        internal static Control Create(Dictionary<GuiLayoutComponents, CheckBox> checks, string prefix, Action changed)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 6, 0, 8) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var values = new[] { GuiLayoutComponents.Layout, GuiLayoutComponents.Hotbars, GuiLayoutComponents.Keybindings, GuiLayoutComponents.PetNames };
            var labels = new[] { L("Window layout", "Vindueslayout"), L("Hotbars", "Hotbars"), L("Key bindings", "Tastaturgenveje"), L("Pet names", "Kæledyrsnavne") };
            for (int i = 0; i < values.Length; i++)
            {
                var box = new NexusCheckBox { Name = prefix + values[i], Text = labels[i], AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 3, 8, 3) };
                box.CheckedChanged += (sender, e) => changed?.Invoke(); checks[values[i]] = box; panel.Controls.Add(box, i % 2, i / 2);
            }
            return panel;
        }
        internal static GuiLayoutComponents Selected(Dictionary<GuiLayoutComponents, CheckBox> checks)
            => checks.Where(pair => pair.Value.Checked).Aggregate((GuiLayoutComponents)0, (value, pair) => value | pair.Key);
        internal static string Describe(GuiLayoutComponents components)
        {
            var names = new List<string>();
            if ((components & GuiLayoutComponents.Layout) != 0) names.Add(L("window layout", "vindueslayout"));
            if ((components & GuiLayoutComponents.Hotbars) != 0) names.Add(L("hotbars", "hotbars"));
            if ((components & GuiLayoutComponents.Keybindings) != 0) names.Add(L("key bindings", "tastaturgenveje"));
            if ((components & GuiLayoutComponents.PetNames) != 0) names.Add(L("pet names", "kæledyrsnavne"));
            return "\r\n" + L("Components: ", "Dele: ") + string.Join(", ", names) + ((components & GuiLayoutComponents.Hotbars) != 0
                ? "\r\n" + L("Hotbar actions may refer to nanos, items or macros unavailable on another char.", "Hotbar-handlinger kan henvise til nanoer, genstande eller makroer, som en anden char ikke har.") : "");
        }
    }
    internal sealed class GuiTemplateNameDialog : EditorDialog
    {
        private readonly TextBox name;
        internal string Value => name.Text.Trim();
        internal GuiTemplateNameDialog(string initial) : base(L("Rename template", "Omdøb skabelon"), 220)
        {
            name = AddTextField(L("Template name", "Skabelonnavn"), initial); name.MaxLength = 80;
            SaveButton.Click += (sender, e) => { if (Value.Length > 0) DialogResult = DialogResult.OK; };
        }
    }
}
