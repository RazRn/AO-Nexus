using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed partial class GuiManagerDialog
    {
        private readonly Label layoutEmpty = new Label(), skinEmpty = new Label(), skinReview = new Label();
        private Button copyLayout, rolloutLayouts, rolloutSkins, manageLayout, captureLayoutButton, defaultSkinButton;
        private string activeSkinName;

        private static Control TaskIntro(string title, string description)
        {
            var intro = Stack(Auto(), Auto()); intro.Dock = DockStyle.Top; intro.AutoSize = true;
            intro.Margin = new Padding(0, 0, 0, 10);
            intro.Controls.Add(new Label { Text = title, AutoSize = true, Dock = DockStyle.Top,
                ForeColor = Theme.Foreground, Font = new Font("Segoe UI", 17F, FontStyle.Bold), UseMnemonic = false, Margin = Padding.Empty }, 0, 0);
            var detail = Note(description); detail.Margin = new Padding(0, 2, 0, 0); intro.Controls.Add(detail, 0, 1);
            return intro;
        }
        private static Label SectionHeading(string text) => new Label { Text = text, AutoSize = true, Dock = DockStyle.Top,
            ForeColor = Theme.Accent, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
        private static Panel Card()
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Surface,
                Margin = new Padding(0, 0, 10, 0) };
            panel.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1); };
            return panel;
        }
        private static void DetailLabel(Label label, string name, int height)
        {
            label.Name = name; label.Dock = DockStyle.Fill; label.Height = height;
            label.ForeColor = Theme.Muted; label.Margin = new Padding(0, 8, 0, 6);
        }
        private static Control ComboRow(ComboBox combo)
        {
            combo.Dock = DockStyle.Top; combo.Margin = Padding.Empty;
            var row = new Panel { Dock = DockStyle.Top, Height = 38, Margin = new Padding(0, 0, 0, 6) };
            row.Controls.Add(combo);
            combo.SizeChanged += (s, e) => row.Height = combo.Height + NexusChrome.Scale(combo, 8);
            return row;
        }
        private void BuildLayouts()
        {
            var page = Page(L("Layouts", "Layouts")); page.Name = "guiLayoutsPage";
            var layout = Stack(Auto(), Fill(), Auto()); page.Controls.Add(layout);
            var intro = TaskIntro(L("Save and reuse character layouts", "Gem og genbrug karakterlayouts"),
                L("Save a character's setup, then reuse it on the characters you choose. You decide which parts to replace.",
                  "Gem en karakters opsætning, og genbrug den på de karakterer, du vælger. Du bestemmer, hvilke dele der erstattes."));
            intro.Name = "guiLayoutsIntro"; layout.Controls.Add(intro, 0, 0);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            columns.RowStyles.Add(Fill());
            layout.Controls.Add(columns, 0, 1);
            var library = Card(); columns.Controls.Add(library, 0, 0);
            var left = Stack(Auto(), Auto(), Fill(), Auto(), Auto()); library.Controls.Add(left);
            left.Controls.Add(SectionHeading(L("1  Choose a saved layout", "1  Vælg et gemt layout")), 0, 0);
            var create = Actions(); left.Controls.Add(create, 0, 1);
            captureLayoutButton = ActionButton(create, L("Save from a character…", "Gem fra en karakter…"), "captureGuiLayout", () => CaptureLayout(false));
            ListStyle(templates, "guiTemplates"); templates.SelectedIndexChanged += (s, e) => UpdateSelection();
            var list = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            list.Controls.Add(SlimScrolling.Wrap(templates));
            layoutEmpty.Name = "guiLayoutEmpty"; layoutEmpty.Text = L("No personal layouts yet.\r\nUse “Save from a character” to add one.", "Ingen personlige layouts endnu.\r\nBrug “Gem fra en karakter” for at tilføje et.");
            layoutEmpty.Dock = DockStyle.Bottom; layoutEmpty.Height = 46; layoutEmpty.ForeColor = Theme.Muted; list.Controls.Add(layoutEmpty);
            left.Controls.Add(list, 0, 2);
            DetailLabel(templateInfo, "guiTemplateInfo", 50); left.Controls.Add(templateInfo, 0, 3);
            defaultTemplate.Name = "defaultGuiTemplate"; defaultTemplate.Text = L("Use for new characters", "Brug til nye karakterer");
            defaultTemplate.AutoSize = false; defaultTemplate.Height = 28; defaultTemplate.Dock = DockStyle.Top;
            defaultTemplate.CheckedChanged += (s, e) => {
                if (updating || preview || busy || !(templates.SelectedItem is TemplateChoice choice)) return;
                try { string value = defaultTemplate.Checked ? choice.Value.Id : null; SaveDefaultTemplate?.Invoke(value); DefaultTemplateId = value;
                    Status(L("Saved for future characters. Existing characters are unchanged.", "Gemt til fremtidige karakterer. Eksisterende karakterer er uændrede.")); }
                catch (Exception error) { Fail(error); }
                UpdateSelection();
            };
            var manage = Actions(); manage.AutoSize = false; manage.Height = 38; manage.WrapContents = false; left.Controls.Add(manage, 0, 4);
            defaultTemplate.Dock = DockStyle.None; defaultTemplate.Width = 220; defaultTemplate.Margin = new Padding(0, 4, 4, 0); manage.Controls.Add(defaultTemplate);
            var rename = ActionButton(manage, L("Rename…", "Omdøb…"), "renameGuiTemplate", RenameTemplate);
            var delete = ActionButton(manage, L("Delete saved layout", "Slet gemt layout"), "deleteGuiTemplate", DeleteTemplate);
            selectionActions.Add(rename); selectionActions.Add(delete); rename.Visible = delete.Visible = false;
            var libraryMenu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground };
            libraryMenu.Items.Add(L("Rename saved layout…", "Omdøb gemt layout…")).Click += async (s, e) => await Safe(RenameTemplate);
            libraryMenu.Items.Add(L("Delete saved layout…", "Slet gemt layout…")).Click += async (s, e) => await Safe(DeleteTemplate);
            manageLayout = Theme.Button(L("Manage ▾", "Rediger ▾")); manageLayout.Name = "guiManageLayout"; manage.Controls.Add(manageLayout);
            manageLayout.Click += (s, e) => libraryMenu.Show(manageLayout, new Point(0, manageLayout.Height));
            Disposed += (s, e) => libraryMenu.Dispose();
            var destination = Card(); destination.Margin = Padding.Empty; columns.Controls.Add(destination, 1, 0);
            var right = Stack(Auto(), Fill(), Auto(), Auto()); destination.Controls.Add(right);
            var heading = Actions(); heading.Controls.Add(SectionHeading(L("2  Choose characters to change", "2  Vælg karakterer, der skal ændres")));
            foreach (bool select in new[] { true, false }) {
                bool check = select;
                var link = new LinkLabel { Text = select ? L("Select all", "Vælg alle") : L("Clear", "Fravælg"), AutoSize = true,
                    Margin = new Padding(8, 2, 4, 4), DisabledLinkColor = Theme.Muted };
                link.Click += (s, e) => { if (!busy) SetAllTargets(check); }; heading.Controls.Add(link);
            }
            right.Controls.Add(heading, 0, 0);
            targets.Name = "guiTargets"; targets.Dock = DockStyle.Fill; targets.CheckBoxes = true; targets.BorderStyle = BorderStyle.None;
            targets.BackColor = Theme.Surface; targets.ForeColor = Theme.Foreground; targets.ItemHeight = 27; targets.HideSelection = false;
            foreach (var server in characters.GroupBy(c => AccountTargets.ServerName(settings, c.Account)).OrderBy(g => g.Key)) {
                var serverNode = new TreeNode(server.Key) { ForeColor = Theme.Accent };
                foreach (var account in server.GroupBy(c => c.Account.Id)) {
                    var accountNode = new TreeNode(account.First().Account.Label);
                    foreach (var choice in account) accountNode.Nodes.Add(new TreeNode(choice.Character.Name) { Tag = choice });
                    serverNode.Nodes.Add(accountNode);
                }
                targets.Nodes.Add(serverNode);
            }
            targets.ExpandAll(); targets.AfterCheck += (s, e) => {
                if (updating) return; updating = true;
                try { foreach (var node in Descendants(e.Node)) node.Checked = e.Node.Checked;
                    for (var parent = e.Node.Parent; parent != null; parent = parent.Parent) parent.Checked = parent.Nodes.Cast<TreeNode>().All(n => n.Checked); }
                finally { updating = false; }
                UpdateSelection();
            };
            var targetHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            targetHost.Controls.Add(SlimScrolling.Wrap(targets));
            if (characters.Count == 0) {
                var emptyTargets = new Label { Name = "guiLayoutTargetsEmpty", Dock = DockStyle.Fill, ForeColor = Theme.Muted,
                    Text = L("No characters added yet.\r\n\r\nAdd an account and its characters in the main window, then return here to copy or apply a layout.",
                        "Ingen karakterer tilføjet endnu.\r\n\r\nTilføj en konto og dens karakterer i hovedvinduet, og vend tilbage for at kopiere eller anvende et layout.") };
                targetHost.Controls.Add(emptyTargets); emptyTargets.BringToFront();
            }
            right.Controls.Add(targetHost, 0, 1);
            right.Controls.Add(SectionHeading(L("3  Choose what to replace", "3  Vælg, hvad der skal erstattes")), 0, 2);
            right.Controls.Add(GuiComponentChoices.Create(componentChecks, "applyGuiComponent", () => { if (!updating) UpdateSelection(); }), 0, 3);
            var review = Stack(new RowStyle(SizeType.Absolute, 48), new RowStyle(SizeType.Absolute, 42)); review.Dock = DockStyle.Top; review.Height = 90; layout.Controls.Add(review, 0, 2);
            DetailLabel(selectedSummary, "guiTargetSummary", 44); review.Controls.Add(selectedSummary, 0, 0);
            var apply = Actions(); apply.AutoSize = false; apply.Height = 38; apply.WrapContents = false; review.Controls.Add(apply, 0, 1);
            applyLayout = ActionButton(apply, L("Review and apply…", "Gennemse og anvend…"), "applyGuiLayout", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id), true);
            copyLayout = ActionButton(apply, L("Copy from a character…", "Kopiér fra en karakter…"), "copyGuiLayout", () => CaptureLayout(true));
            applyDefault = ActionButton(apply, L("Use saved default…", "Brug gemt standard…"), "applyDefaultGui", () => ApplyTemplate(DefaultTemplateId));
            rolloutLayouts = ActionButton(apply, L("Apply to everyone…", "Anvend på alle…"), "rolloutAllGuiLayouts", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id, true));
            applyDefault.Visible = rolloutLayouts.Visible = false;
            var moreMenu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground };
            var savedDefault = moreMenu.Items.Add(L("Apply saved default to selected characters…", "Anvend gemt standard på valgte karakterer…"));
            var applyAll = moreMenu.Items.Add(L("Apply to all configured characters…", "Anvend på alle konfigurerede karakterer…"));
            savedDefault.Click += async (s, e) => await Safe(() => ApplyTemplate(DefaultTemplateId));
            applyAll.Click += async (s, e) => await Safe(() => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id, true));
            var more = Theme.Button(L("More actions ▾", "Flere handlinger ▾")); more.Name = "guiMoreLayoutActions"; apply.Controls.Add(more);
            more.Click += (s, e) => { savedDefault.Enabled = applyDefault.Enabled; applyAll.Enabled = rolloutLayouts.Enabled; moreMenu.Show(more, new Point(0, more.Height)); };
            Disposed += (s, e) => moreMenu.Dispose();
        }

        private void BuildSkins()
        {
            var page = Page(L("Skins", "Skins")); page.Name = "guiSkinsPage";
            var layout = Stack(Auto(), Auto(), Auto(), Fill(), Auto()); page.Controls.Add(layout);
            var intro = TaskIntro(L("Change the look of AO", "Skift udseendet i AO"),
                L("A skin changes colours and artwork for every account sharing an installation. It does not copy character layouts.",
                  "Et skin ændrer farver og grafik for alle konti på samme installation. Det kopierer ikke karakterernes layouts."));
            intro.Name = "guiSkinsIntro"; layout.Controls.Add(intro, 0, 0);
            var targetHeading = SectionHeading(L("1  Choose the installation to change", "1  Vælg installationen, der skal ændres"));
            targetHeading.Name = "guiSkinTargetHeading"; layout.Controls.Add(targetHeading, 0, 1);
            skinTarget.Name = "guiSkinTarget"; skinTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var account in settings.Accounts) {
                try {
                    string root = GuiSkins.ResolvePrefsRoot(account.PrefsPath);
                    var existing = skinTarget.Items.Cast<SkinTargetChoice>().FirstOrDefault(i => string.Equals(i.Root, root, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) { existing = new SkinTargetChoice { Root = root, Name = Path.GetFileName(Path.GetDirectoryName(root)) }; skinTarget.Items.Add(existing); }
                    var owned = characters.Where(c => c.Account.Id == account.Id).ToList();
                    if (owned.Count == 0) owned.Add(new GuiCharacterChoice { Account = account, CharacterPath = "", Label = account.Label });
                    existing.Characters.AddRange(owned);
                } catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException || error is UnauthorizedAccessException) { unavailableSkinAccounts++; }
            }
            skinTarget.SelectedIndexChanged += (s, e) => RefreshSkins(); layout.Controls.Add(ComboRow(skinTarget), 0, 2);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46)); columns.RowStyles.Add(Fill()); layout.Controls.Add(columns, 0, 3);
            var installed = Card(); columns.Controls.Add(installed, 0, 0);
            var left = Stack(Auto(), Fill(), Auto(), Auto()); installed.Controls.Add(left);
            left.Controls.Add(SectionHeading(L("2  Choose an installed skin", "2  Vælg et installeret skin")), 0, 0);
            ListStyle(skins, "guiInstalledSkins"); skins.SelectedIndexChanged += (s, e) => UpdateSelection(); left.Controls.Add(SlimScrolling.Wrap(skins), 0, 1);
            DetailLabel(skinEmpty, "guiSkinEmpty", 40); left.Controls.Add(skinEmpty, 0, 2);
            DetailLabel(skinInfo, "guiSkinInfo", 64); left.Controls.Add(skinInfo, 0, 3);
            var acquire = Card(); acquire.Margin = Padding.Empty; columns.Controls.Add(acquire, 1, 0);
            var right = Stack(Auto(), Auto(), Auto(), Auto(), Auto(), Auto(), Fill()); acquire.Controls.Add(right);
            right.Controls.Add(SectionHeading(L("Add another skin", "Tilføj et andet skin")), 0, 0);
            right.Controls.Add(Note(L("Download or import first. Then select the skin on the left and apply it.", "Download eller importér først. Vælg derefter skinnet til venstre, og anvend det.")), 0, 1);
            catalog.Name = "guiSkinCatalog"; catalog.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var entry in GuiSkins.Catalog) catalog.Items.Add(new CatalogChoice { Value = entry }); if (catalog.Items.Count > 0) catalog.SelectedIndex = 0;
            right.Controls.Add(ComboRow(catalog), 0, 2);
            clientEngine.Name = "guiSkinEngine"; clientEngine.DropDownStyle = ComboBoxStyle.DropDownList;
            clientEngine.Items.AddRange(new object[] { L("Classic client package", "Pakke til klassisk klient"), L("New engine package", "Pakke til ny grafikmotor") }); clientEngine.SelectedIndex = 0;
            right.Controls.Add(ComboRow(clientEngine), 0, 3);
            var downloads = Actions(); right.Controls.Add(downloads, 0, 4);
            skinActions.Add(ActionButton(downloads, L("Download skin", "Download skin"), "downloadGuiSkin", DownloadSkin));
            skinActions.Add(ActionButton(downloads, L("Import ZIP…", "Importér ZIP…"), "importGuiSkin", ImportSkin));
            var website = Actions(); right.Controls.Add(website, 0, 5);
            ActionButton(website, L("View skin website ↗", "Se skin-hjemmeside ↗"), "guiSkinWebsite", () => {
                if (catalog.SelectedItem is CatalogChoice entry && !preview) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Value.SourceUrl) { UseShellExecute = true });
                return Task.CompletedTask;
            });
            var bottom = Stack(new RowStyle(SizeType.Absolute, 58), new RowStyle(SizeType.Absolute, 42)); bottom.Dock = DockStyle.Top; bottom.Height = 100; layout.Controls.Add(bottom, 0, 4);
            DetailLabel(skinReview, "guiSkinReview", 44); bottom.Controls.Add(skinReview, 0, 0);
            var apply = Actions(); apply.AutoSize = false; apply.Height = 38; apply.WrapContents = false; bottom.Controls.Add(apply, 0, 1);
            applySkin = ActionButton(apply, L("Review and apply skin…", "Gennemse og anvend skin…"), "applyGuiSkin", ApplySkin, true);
            defaultSkinButton = ActionButton(apply, L("Restore AO's original skin…", "Brug AO's oprindelige skin…"), "defaultGuiSkin", () => ApplySkinName("Default"));
            skinActions.Add(defaultSkinButton);
            rolloutSkins = ActionButton(apply, L("Apply to all…", "Anvend på alle…"), "rolloutAllGuiSkins", RolloutSkin);
            if (skinTarget.Items.Count > 0) skinTarget.SelectedIndex = 0;
        }

        private void DesignSelectionUpdate()
        {
            layoutEmpty.Visible = templates.Items.Count <= 1;
            if (captureLayoutButton != null) captureLayoutButton.Enabled = !busy && characters.Count > 0;
            if (manageLayout != null) manageLayout.Enabled = !busy && templates.SelectedItem is TemplateChoice personal && personal.Value.Id != GuiLayouts.GameDefaultId;
            bool selected = SelectedCharacters().Count > 0;
            if (copyLayout != null) copyLayout.Enabled = !busy && selected;
            if (rolloutLayouts != null) rolloutLayouts.Enabled = !busy && templates.SelectedItem != null && characters.Count > 0 && GuiComponentChoices.Selected(componentChecks) != 0;
            if (rolloutSkins != null) rolloutSkins.Enabled = !busy && skins.SelectedItem != null && skinTarget.Items.Count > 1;
            skinEmpty.Visible = skinTarget.SelectedItem == null || skins.Items.Count == 0;
            skinEmpty.Text = skinTarget.SelectedItem == null
                ? L("No installation is ready. Select an account in the main window, choose Edit, then set its Prefs folder.", "Ingen installation er klar. Vælg en konto i hovedvinduet, tryk Rediger, og vælg dens Prefs-mappe.")
                : L("No skins found here. Download or import one using the panel on the right.", "Ingen skins fundet her. Download eller importér et i panelet til højre.");
            var target = skinTarget.SelectedItem as SkinTargetChoice;
            bool alreadyActive = activeSkinName != null && string.Equals(activeSkinName, skins.SelectedItem as string, StringComparison.OrdinalIgnoreCase);
            if (applySkin != null) applySkin.Enabled = !busy && target != null && skins.SelectedItem != null && !alreadyActive;
            if (defaultSkinButton != null) defaultSkinButton.Enabled = !busy && target != null && activeSkinName != null && !string.Equals(activeSkinName, "Default", StringComparison.OrdinalIgnoreCase);
            skinReview.Text = target == null ? L("Choose an installation to get started.", "Vælg en installation for at begynde.")
                : L("Applies to: ", "Gælder for: ") + string.Join(", ", target.Characters.Select(c => c.Account.Label).Distinct())
                    + "\r\n" + (alreadyActive ? L("This skin is already active. Choose another skin or add one on the right.", "Dette skin er allerede aktivt. Vælg et andet, eller tilføj et til højre.")
                        : L("Nexus saves the previous skin selection and reviews any required logout before applying.", "Nexus gemmer det tidligere skin-valg og gennemgår eventuelt logout før ændringen."));
        }
    }
}
