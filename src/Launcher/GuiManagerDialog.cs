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

    internal sealed partial class GuiManagerDialog : EditorDialog
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
        private readonly ListBox scriptList = new ListBox(), macroList = new ListBox();
        private readonly ComboBox scriptTarget = new NexusComboBox(), scriptCopyTarget = new NexusComboBox(), macroSource = new NexusComboBox();
        private readonly TextBox scriptPreview = new NexusTextBox();
        private readonly Label scriptInfo = new Label(), macroInfo = new Label();
        private readonly List<Button> scriptActions = new List<Button>();
        private Button copyMacros;
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
        private string ScriptBackupRoot => Path.Combine(libraryRoot, "ScriptBackups");

        internal GuiManagerDialog(LauncherSettings settings, string libraryRoot, bool preview = false)
            : base(L("Game GUI", "Spillets GUI"), 700)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.libraryRoot = libraryRoot ?? throw new ArgumentNullException(nameof(libraryRoot)); this.preview = preview;
            DefaultTemplateId = settings.DefaultGuiTemplateId;
            ClientSize = new Size(1120, 740); MinimumSize = new Size(980, 700);
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            DialogLayout.Padding = new Padding(18); DialogLayout.RowStyles[1].Height = 30;
            DialogLayout.ColumnStyles.Clear(); DialogLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
            BuildLayouts(); BuildSkins(); BuildScripts(); BuildBackups();
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
        private static FlowLayoutPanel Actions() => new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 4) };
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
        private static IEnumerable<TreeNode> Descendants(TreeNode parent) { foreach (TreeNode child in parent.Nodes) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
        private IEnumerable<TreeNode> Leaves() => targets.Nodes.Cast<TreeNode>().SelectMany(node => Descendants(node)).Where(node => node.Tag is GuiCharacterChoice);
        internal List<GuiCharacterChoice> SelectedCharacters() => Leaves().Where(node => node.Checked).Select(node => (GuiCharacterChoice)node.Tag).ToList();
        private void SetAllTargets(bool value) { updating = true; foreach (TreeNode node in targets.Nodes) { node.Checked = value; foreach (var child in Descendants(node)) child.Checked = value; } updating = false; UpdateSelection(); }

        private List<string> SelectedScripts() => scriptList.SelectedItems.Cast<GuiScriptInfo>().Select(item => item.Name).ToList();
        private void RefreshScripts(IEnumerable<string> select = null)
        {
            try
            {
                var keep = new HashSet<string>(select ?? SelectedScripts(), StringComparer.OrdinalIgnoreCase);
                scriptList.Items.Clear();
                if (scriptTarget.SelectedItem is SkinTargetChoice target)
                {
                    var items = GuiScripts.List(target.Root);
                    foreach (var item in items) scriptList.Items.Add(item);
                    for (int i = 0; i < scriptList.Items.Count; i++) if (keep.Contains(((GuiScriptInfo)scriptList.Items[i]).Name)) scriptList.SetSelected(i, true);
                    if (scriptList.SelectedIndex < 0 && scriptList.Items.Count > 0) scriptList.SetSelected(0, true);
                    scriptInfo.Text = items.Count + (items.Count == 1 ? L(" script in ", " script i ") : L(" scripts in ", " scripts i ")) + target.Name
                        + " · " + L("shared by: ", "deles af: ") + string.Join(", ", target.Characters.Select(choice => choice.Account.Label).Distinct());
                }
                else scriptInfo.Text = L("Choose an account Prefs folder first in Edit account.", "Vælg først kontoens Prefs-mappe under Rediger konto.");
            }
            catch (Exception error) { scriptInfo.Text = error.Message; }
            ShowScript();
        }
        private void ShowScript()
        {
            try
            {
                if (scriptTarget.SelectedItem is SkinTargetChoice target && scriptList.SelectedItems.Count == 1 && scriptList.SelectedItem is GuiScriptInfo item)
                    scriptPreview.Text = GuiScripts.Preview(GuiScripts.ReadScript(target.Root, item.Name)).Replace("\r\n", "\n").Replace("\n", "\r\n");
                else scriptPreview.Text = scriptList.SelectedItems.Count > 1 ? scriptList.SelectedItems.Count + L(" scripts selected.", " scripts valgt.") : "";
            }
            catch (Exception error) { scriptPreview.Text = error.Message; }
            UpdateSelection();
        }
        private void RefreshMacros()
        {
            macroList.Items.Clear();
            try
            {
                if (macroSource.SelectedItem is GuiCharacterChoice choice && !string.IsNullOrEmpty(choice.CharacterPath) && Directory.Exists(choice.CharacterPath))
                {
                    var macros = TextMacros.Read(choice.CharacterPath);
                    foreach (var macro in macros) macroList.Items.Add(macro);
                    macroInfo.Text = macros.Count + (macros.Count == 1 ? L(" macro", " makro") : L(" macros", " makroer")) + L(". AO saves macro changes on normal logout.", ". AO gemmer makroændringer ved normalt logout.");
                }
                else macroInfo.Text = L("Prefs missing: log in once, then choose the account folder in Edit account.", "Prefs mangler: log ind én gang, og vælg derefter kontomappen under Rediger konto.");
            }
            catch (Exception error) { macroInfo.Text = error.Message; }
            UpdateSelection();
        }
        private async Task CopyScripts(bool all)
        {
            if (!(scriptTarget.SelectedItem is SkinTargetChoice source)) return;
            var names = SelectedScripts(); if (names.Count == 0) { Status(L("Select one or more scripts first.", "Vælg et eller flere scripts først.")); return; }
            var destinations = (all ? scriptTarget.Items.Cast<SkinTargetChoice>() : new[] { scriptCopyTarget.SelectedItem as SkinTargetChoice })
                .Where(item => item != null && !string.Equals(item.Root, source.Root, StringComparison.OrdinalIgnoreCase)).ToList();
            if (destinations.Count == 0) { Status(L("Choose another installation to copy to.", "Vælg en anden installation at kopiere til.")); return; }
            var overwritten = new List<string>();
            var sourceHashes = GuiScripts.List(source.Root).ToDictionary(item => item.Name, item => item.Hash, StringComparer.OrdinalIgnoreCase);
            foreach (var target in destinations)
                foreach (var existing in GuiScripts.List(target.Root).Where(item => names.Contains(item.Name, StringComparer.OrdinalIgnoreCase) && sourceHashes.TryGetValue(item.Name, out string hash) && hash != item.Hash))
                    overwritten.Add(existing.Name + " — " + target.Name);
            string question = L("Copy these scripts?", "Kopiér disse scripts?") + "\r\n" + string.Join("\r\n", names.Take(15).Select(name => "• " + name)) + (names.Count > 15 ? "\r\n+ " + (names.Count - 15) : "")
                + "\r\n\r\n" + L("To: ", "Til: ") + string.Join(", ", destinations.Select(item => item.Name))
                + (overwritten.Count == 0 ? "" : "\r\n\r\n" + L("Different scripts with the same name are replaced (a backup is kept):", "Andre scripts med samme navn erstattes (der gemmes en sikkerhedskopi):") + "\r\n" + string.Join("\r\n", overwritten.Take(10).Select(name => "• " + name)));
            if (NexusMessageBox.Show(this, question, L("Scripts", "Scripts"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            await RunScripts(() =>
            {
                var applied = new List<Tuple<SkinTargetChoice, GuiScriptBackup>>();
                try { foreach (var target in destinations) applied.Add(Tuple.Create(target, GuiScripts.Copy(source.Root, names, target.Root, ScriptBackupRoot))); }
                catch (Exception failure)
                {
                    var errors = new List<string>();
                    foreach (var item in applied.AsEnumerable().Reverse().Where(pair => !string.IsNullOrEmpty(pair.Item2.Id)))
                        try { GuiScripts.Restore(item.Item1.Root, item.Item2.Id, ScriptBackupRoot); } catch (Exception rollback) { errors.Add(item.Item1.Name + ": " + rollback.Message); }
                    throw new IOException(failure.Message + "\r\n" + (errors.Count == 0 ? L("Earlier installations in this copy were restored.", "Tidligere installationer i denne kopiering blev gendannet.")
                        : L("Some installations need manual backup restoration:\r\n", "Nogle installationer kræver manuel gendannelse fra sikkerhedskopi:\r\n") + string.Join("\r\n", errors)), failure);
                }
            }, L("Scripts copied. Use them in game with /name.", "Scripts kopieret. Brug dem i spillet med /navn."));
        }
        private async Task ImportScript()
        {
            if (!(scriptTarget.SelectedItem is SkinTargetChoice target)) return;
            using (var picker = new OpenFileDialog { Filter = L("Script files (*.*)|*.*", "Script-filer (*.*)|*.*"), CheckFileExists = true, Multiselect = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                var files = picker.FileNames.ToList();
                var invalid = files.Where(file => !GuiScripts.ValidName(Path.GetFileName(file))).ToList();
                if (invalid.Count > 0) { Status(L("Not imported: ", "Ikke importeret: ") + string.Join(", ", invalid.Select(Path.GetFileName)) + L(". Use letters, digits, '-', '_' or '.', and no program files.", ". Brug bogstaver, tal, '-', '_' eller '.', og ingen programfiler."), true); return; }
                var existing = GuiScripts.List(target.Root).Select(item => item.Name).ToList();
                var replaced = files.Select(Path.GetFileName).Where(name => existing.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (replaced.Count > 0 && NexusMessageBox.Show(this, L("Replace these scripts? A backup is kept.", "Erstat disse scripts? Der gemmes en sikkerhedskopi.") + "\r\n" + string.Join("\r\n", replaced.Select(name => "• " + name)), L("Scripts", "Scripts"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                await RunScripts(() =>
                {
                    var applied = new List<GuiScriptBackup>();
                    try { foreach (string file in files) applied.Add(GuiScripts.Import(target.Root, file, ScriptBackupRoot)); }
                    catch { foreach (var backup in applied.AsEnumerable().Reverse().Where(item => !string.IsNullOrEmpty(item.Id))) try { GuiScripts.Restore(target.Root, backup.Id, ScriptBackupRoot); } catch { } throw; }
                }, L("Script imported.", "Script importeret."), files.Select(Path.GetFileName));
            }
        }
        private async Task DeleteScripts()
        {
            if (!(scriptTarget.SelectedItem is SkinTargetChoice target)) return;
            var names = SelectedScripts(); if (names.Count == 0) return;
            if (NexusMessageBox.Show(this, L("Delete these scripts from this installation? A backup is kept.", "Slet disse scripts fra installationen? Der gemmes en sikkerhedskopi.") + "\r\n" + string.Join("\r\n", names.Take(15).Select(name => "• " + name)), target.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            await RunScripts(() => GuiScripts.Delete(target.Root, names, ScriptBackupRoot), L("Scripts deleted. Restore them under Backups.", "Scripts slettet. Gendan dem under Sikkerhedskopier."));
        }
        private Task OpenScriptFolder()
        {
            if (!(scriptTarget.SelectedItem is SkinTargetChoice target)) return Task.CompletedTask;
            string folder = GuiScripts.ScriptsRoot(target.Root); Directory.CreateDirectory(folder); GuiScripts.SafePath(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            return Task.CompletedTask;
        }
        private async Task RunScripts(Action action, string done, IEnumerable<string> select = null)
        {
            SetBusy(true); Status(L("Updating scripts…", "Opdaterer scripts…"));
            try { await Task.Run(action); Status(done); }
            finally { SetBusy(false); RefreshScripts(select); RefreshBackups(); }
        }
        private Task CopyMacros() => CopyMacroSelection();
        private async Task Safe(Func<Task> action)
        {
            if (busy) return;
            try { if (preview) { Status(L("Preview: no game files are changed.", "Forhåndsvisning: spilfiler ændres ikke.")); return; } await action(); }
            catch (Exception error) { Fail(error); }
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
        private void Status(string message, bool error = false)
        {
            ErrorLabel.Text = message; ErrorLabel.ForeColor = error ? Theme.Warning : Theme.Muted;
            if (error) DiagnosticJournal.Record("gui", "Game GUI dialog: " + message);
        }
        private void Fail(Exception error)
        {
            DiagnosticJournal.Exception("gui", "Game GUI dialog", error);
            ErrorLabel.Text = error.Message; ErrorLabel.ForeColor = Theme.Warning;
        }
        private async Task<bool> Run(string description, List<GuiCharacterChoice> choices, bool installationWide, Action action)
        {
            if (ExecuteOperation == null) { Status(L("This operation is unavailable in preview.", "Handlingen er ikke tilgængelig i forhåndsvisning.")); return false; }
            SetBusy(true); Status(description.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0]);
            try { bool result = await ExecuteOperation(description, choices, installationWide, action); if (result) Status(L("Operation completed.", "Handlingen er udført.")); return result; }
            finally { SetBusy(false); }
        }
        private async Task CaptureLayout(bool copy, GuiLayoutComponents initial = GuiLayoutComponents.Layout, GuiCharacterChoice initialSource = null)
        {
            var selected = UniqueTargets(SelectedCharacters()); if (copy && selected.Count == 0) { Status(L("Select target chars first.", "Vælg mål-chars først.")); return; }
            using (var dialog = new GuiCaptureDialog(characters, copy, initial, initialSource))
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
                RefreshLibrary(captured?.Id); RefreshMacros();
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
            else if (choice.Script != null)
            {
                if (NexusMessageBox.Show(this, L("Restore the scripts changed by this backup? Current versions are backed up first.", "Gendan de scripts, som denne sikkerhedskopi ændrede? De nuværende udgaver sikkerhedskopieres først.") + "\r\n" + string.Join("\r\n", choice.Script.Changes.Take(15).Select(change => "• " + change.Name)), choice.SkinTarget.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                await RunScripts(() => GuiScripts.Restore(choice.SkinTarget.Root, choice.Script.Id, ScriptBackupRoot), L("Scripts restored.", "Scripts gendannet."));
            }
            else await Run(L("Restore skin selection", "Gendan skin-valg"), choice.SkinTarget.Characters.ToList(), true, () => GuiSkins.RestoreSelection(choice.SkinTarget.Root, choice.Skin.Directory));
            RefreshSkins(); RefreshBackups(); RefreshMacros();
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
                RefreshSkins(); RefreshScripts(); RefreshMacros(); RefreshBackups(); UpdateSelection();
                if (preview) Status(L("Preview uses example accounts. No game files will be changed.", "Forhåndsvisning bruger eksempelkontoer. Spilfiler ændres ikke."));
            }
            catch (Exception error) { Fail(error); }
        }
        private void RefreshSkins(string selectedName = null)
        {
            activeSkinName = null;
            try
            {
                skins.Items.Clear();
                if (skinTarget.SelectedItem is SkinTargetChoice target)
                {
                    foreach (string name in GuiSkins.InstalledSkins(target.Root)) skins.Items.Add(name);
                    activeSkinName = GuiSkins.CurrentSkin(target.Root);
                    skins.SelectedItem = selectedName ?? activeSkinName;
                    if (skins.SelectedIndex < 0 && skins.Items.Count > 0) skins.SelectedIndex = 0;
                    skinInfo.Text = L("Active skin: ", "Aktivt skin: ") + activeSkinName
                        + "\r\n" + L("Select a different skin, then review the change below.", "Vælg et andet skin, og gennemse ændringen nedenfor.");
                    if (unavailableSkinAccounts > 0) skinInfo.Text += "\r\n" + unavailableSkinAccounts + L(" account(s) need an initialized Prefs folder. Choose it under Edit account.", " konti mangler en initialiseret Prefs-mappe. Vælg den under Rediger konto.");
                }
                else skinInfo.Text = L("Choose an account Prefs folder first in Edit account. No valid Prefs installation is available.", "Vælg først kontoens Prefs-mappe under Rediger konto. Ingen gyldig Prefs-installation er tilgængelig.");
            }
            catch (Exception error) { skinInfo.Text = error.Message; }
            UpdateSelection();
        }
        private void RefreshBackups()
        {
            var entries = new List<BackupChoice>();
            foreach (var choice in characters.Where(item => !string.IsNullOrEmpty(item.CharacterPath) && Directory.Exists(item.CharacterPath)).GroupBy(item => item.CharacterPath, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
                foreach (var backup in GuiLayouts.ListBackups(libraryRoot, choice.CharacterPath)) entries.Add(new BackupChoice { Layout = backup, Character = choice, When = backup.CreatedUtc });
            foreach (var target in skinTarget.Items.Cast<SkinTargetChoice>())
                foreach (var backup in GuiSkins.ListBackups(target.Root, SkinBackupRoot)) entries.Add(new BackupChoice { Skin = backup, SkinTarget = target, When = backup.CreatedUtc });
            foreach (var target in skinTarget.Items.Cast<SkinTargetChoice>())
                try { foreach (var backup in GuiScripts.ListBackups(target.Root, ScriptBackupRoot)) entries.Add(new BackupChoice { Script = backup, SkinTarget = target, When = backup.CreatedUtc }); }
                catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is ArgumentException) { }
            backupChoices.Clear(); backupChoices.AddRange(entries.OrderByDescending(entry => entry.When).Take(200));
            FilterBackups();
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
            templateInfo.Text = builtIn ? L("Reset window positions and visibility to AO defaults. Hotbars, keys, macros and skin stay unchanged.", "Nulstil vinduesplaceringer og synlighed til AO-standard. Hotbars, taster, makroer og skin bevares.") : template == null ? L("Save a character's layout to create your first reusable setup.", "Gem en karakters layout for at oprette din første genbrugelige opsætning.")
                : L("Saved from: ", "Gemt fra: ") + template.SourceCharacter + " · " + template.CreatedUtc.ToLocalTime().ToString("g")
                    + "\r\n" + L("Only settings included in this layout can be selected.", "Kun indstillinger gemt i dette layout kan vælges.");
            bool previousUpdating = updating; updating = true;
            foreach (var pair in componentChecks)
            {
                pair.Value.Enabled = !busy && template != null && (template.Components & pair.Key) != 0;
                if (componentTemplateId != template?.Id) pair.Value.Checked = template != null && (template.Components & pair.Key) != 0;
            }
            componentTemplateId = template?.Id; defaultTemplate.Checked = template != null && template.Id == DefaultTemplateId; updating = previousUpdating;
            defaultTemplate.Enabled = !busy && !preview && template != null && !builtIn;
            selectedSummary.Text = selected.Count == 0 ? L("Choose the characters you want to change. Your login selection stays separate.", "Vælg de karakterer, du vil ændre. Dine loginvalg holdes adskilt.")
                : selected.Count + L(" characters will receive: ", " karakterer modtager: ") + (template?.Name ?? L("no layout selected", "intet layout valgt"));
            selectedSummary.Text += "\r\n" + (selected.Any(choice => string.IsNullOrEmpty(choice.CharacterPath) || !Directory.Exists(choice.CharacterPath))
                    ? L("Missing Prefs: log in once, then choose the account folder in Edit account.", "Manglende Prefs: log ind én gang, og vælg derefter kontomappen under Rediger konto.")
                    : L("Review affected characters before applying. Previous settings are backed up automatically.", "Gennemse berørte karakterer før ændringen. Tidligere indstillinger sikkerhedskopieres automatisk."));
            if (selected.Count > 0 && GuiComponentChoices.Selected(componentChecks) == 0)
                selectedSummary.Text = L("Choose at least one part to replace in step 3.", "Vælg mindst én del, der skal erstattes, i trin 3.");
            applyLayout.Enabled = !busy && template != null && selected.Count > 0 && GuiComponentChoices.Selected(componentChecks) != 0;
            applyDefault.Enabled = !busy && !string.IsNullOrEmpty(DefaultTemplateId) && selected.Count > 0;
            foreach (var button in selectionActions) button.Enabled = !busy && template != null && !builtIn;
            if (applySkin != null) applySkin.Enabled = !busy && skinTarget.SelectedItem != null && skins.SelectedItem != null;
            foreach (var button in skinActions) button.Enabled = !busy && skinTarget.SelectedItem != null;
            foreach (var button in scriptActions) button.Enabled = !busy && scriptTarget.SelectedItem != null;
            if (copyMacros != null) copyMacros.Enabled = !busy && macroSource.SelectedItem != null && macroList.Items.Count > 0;
            if (restoreBackup != null) restoreBackup.Enabled = !busy && backups.SelectedItem != null;
            var backup = backups.SelectedItem as BackupChoice;
            backupInfo.Text = backup == null ? L("Backups appear after your first change.", "Sikkerhedskopier vises efter din første ændring.")
                : backup.Layout != null ? backup.Character.Label + "\r\n" + backup.Layout.TargetPath + "\r\n" + backup.Layout.State
                : backup.Script != null ? backup.Script.ScriptsRoot + "\r\n" + ScriptAction(backup.Script.Action) + ": " + string.Join(", ", backup.Script.Changes.Take(8).Select(change => change.Name)) + (backup.Script.Changes.Count > 8 ? " …" : "") + "\r\n" + backup.Script.State
                : backup.SkinTarget.Root + "\r\n" + L("Previous skin: ", "Tidligere skin: ") + backup.Skin.SkinName;
            DesignSelectionUpdate(); ScriptWorkflowUpdate(); BackupWorkflowUpdate();
        }
        private static string ScriptAction(string action)
            => action == "Copy" ? L("Copied", "Kopieret") : action == "Import" ? L("Imported", "Importeret") : action == "Delete" ? L("Deleted", "Slettet") : action == "Restore" ? L("Restored", "Gendannet") : action;
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
            internal GuiLayoutBackup Layout; internal GuiSkinBackup Skin; internal GuiScriptBackup Script; internal GuiCharacterChoice Character; internal SkinTargetChoice SkinTarget; internal DateTime When;
            public override string ToString() => When.ToLocalTime().ToString("g") + " · " + (Layout != null ? L("Layout — ", "Layout — ") + Character.Label : Script != null ? L("Scripts — ", "Scripts — ") + SkinTarget.Name : L("Skin — ", "Skin — ") + SkinTarget.Name);
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
        internal GuiCaptureDialog(IEnumerable<GuiCharacterChoice> characters, bool copy) : this(characters, copy, GuiLayoutComponents.Layout, null) { }
        internal GuiCaptureDialog(IEnumerable<GuiCharacterChoice> characters, bool copy, GuiLayoutComponents initial, GuiCharacterChoice initialSource) : base(copy ? L("Copy a character's setup", "Kopiér en karakters opsætning") : L("Save a character's layout", "Gem en karakters layout"), 520)
        {
            ClientSize = new Size(620, 600);
            AddWrappedNote(L("Choose the character whose setup you want to reuse. AO saves changes on normal logout; Nexus will review any required logout with you before copying.", "Vælg den karakter, hvis opsætning du vil genbruge. AO gemmer ved normalt logout; Nexus gennemgår eventuelt logout med dig før kopiering."), "captureGuiGuide");
            Fields.Controls.Add(new Label { Text = L("Source character", "Kildekarakter"), AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Accent, Margin = new Padding(0, 8, 0, 6) });
            source = new NexusComboBox { Name = "guiCaptureSource", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var character in characters) source.Items.Add(character); Fields.Controls.Add(source);
            if (initialSource != null && source.Items.Contains(initialSource)) source.SelectedItem = initialSource;
            name = AddTextField(L("Name for this saved layout", "Navn på dette gemte layout"), copy ? "Character copy" : ""); name.MaxLength = 80;
            if (copy) name.Parent.Visible = false;
            Fields.Controls.Add(new Label { Text = L("Include in the copy", "Medtag i kopien"), AutoSize = true, Dock = DockStyle.Top, ForeColor = Theme.Accent, Margin = new Padding(0, 8, 0, 6) });
            Fields.Controls.Add(GuiComponentChoices.Create(components, "captureGuiComponent", null));
            foreach (var pair in components) pair.Value.Checked = (initial & pair.Key) != 0;
            // Hotbar macro buttons point at macro numbers; keep both together by default.
            components[GuiLayoutComponents.Hotbars].CheckedChanged += (sender, e) => { if (components[GuiLayoutComponents.Hotbars].Checked) components[GuiLayoutComponents.Macros].Checked = true; };
            AddWrappedNote(L("Hotbars can refer to nanos or items that another char does not have. Macros replace the target's macros, so hotbar buttons keep working. Include these only when you want to replace them.", "Hotbars kan henvise til nanoer eller genstande, som en anden char ikke har. Makroer erstatter målets makroer, så hotbar-knapper bliver ved med at virke. Medtag dem kun, når du vil erstatte dem."), "captureGuiComponentsGuide");
            useDefault = new NexusCheckBox { Name = "newGuiTemplateDefault", Text = L("Use for new characters", "Brug til nye karakterer"), AutoSize = false, Height = 28, Dock = DockStyle.Top, Visible = !copy };
            Fields.Controls.Add(useDefault);
            AddWrappedNote(L("Only supported interface settings are copied. Existing characters are changed only when you select them as targets.", "Kun understøttede interfaceindstillinger kopieres. Eksisterende chars ændres kun, når du vælger dem som mål."), "captureGuiScope");
            SaveButton.Text = copy ? L("Review copy…", "Gennemse kopi…") : L("Save layout…", "Gem layout…");
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
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = new Padding(0, 6, 0, 8) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var values = new[] { GuiLayoutComponents.Layout, GuiLayoutComponents.Hotbars, GuiLayoutComponents.Keybindings, GuiLayoutComponents.PetNames, GuiLayoutComponents.Macros };
            var labels = new[] { L("Window layout", "Vindueslayout"), L("Hotbars", "Hotbars"), L("Key bindings", "Tastaturgenveje"), L("Pet names", "Kæledyrsnavne"), L("Macros", "Makroer") };
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
            if ((components & GuiLayoutComponents.Macros) != 0) names.Add(L("macros", "makroer"));
            bool hotbars = (components & GuiLayoutComponents.Hotbars) != 0, macros = (components & GuiLayoutComponents.Macros) != 0;
            return "\r\n" + L("Components: ", "Dele: ") + string.Join(", ", names)
                + (hotbars ? "\r\n" + L("Hotbar actions may refer to nanos or items unavailable on another char.", "Hotbar-handlinger kan henvise til nanoer eller genstande, som en anden char ikke har.") : "")
                + (hotbars && !macros ? "\r\n" + L("Macros are not included, so macro buttons may point at other macros.", "Makroer er ikke med, så makroknapper kan pege på andre makroer.") : "")
                + (macros ? "\r\n" + L("The targets' macros are replaced (a backup is kept).", "Målenes makroer erstattes (der gemmes en sikkerhedskopi).") : "");
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
