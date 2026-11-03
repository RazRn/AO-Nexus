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
            var page = Page(AOLauncher.Localization.Texts.Get("Text_Layouts_8D1D0CC42E06")); page.Name = "guiLayoutsPage";
            var layout = Stack(Auto(), Fill(), Auto()); page.Controls.Add(layout);
            var intro = TaskIntro(AOLauncher.Localization.Texts.Get("Text_Save_and_reuse_character_layouts_10B6DDB990D6"),
                AOLauncher.Localization.Texts.Get("Text_Save_a_character_s_setup_then_C788DF0E1F68"));
            intro.Name = "guiLayoutsIntro"; layout.Controls.Add(intro, 0, 0);
            // Three steps side by side: saved layout → characters → what to replace and apply.
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            columns.RowStyles.Add(Fill());
            layout.Controls.Add(columns, 0, 1);
            var library = Card(); columns.Controls.Add(library, 0, 0);
            var left = Stack(Auto(), Auto(), Fill(), Auto(), Auto()); library.Controls.Add(left);
            left.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_1_Choose_a_saved_layout_1ECA0480844E")), 0, 0);
            var create = Actions(); left.Controls.Add(create, 0, 1);
            captureLayoutButton = ActionButton(create, AOLauncher.Localization.Texts.Get("Text_Save_from_a_character_07CE27FEB808"), "captureGuiLayout", () => CaptureLayout(false));
            ListStyle(templates, "guiTemplates"); templates.SelectedIndexChanged += (s, e) => UpdateSelection();
            var list = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            list.Controls.Add(SlimScrolling.Wrap(templates));
            layoutEmpty.Name = "guiLayoutEmpty"; layoutEmpty.Text = AOLauncher.Localization.Texts.Get("Text_No_personal_layouts_yet_Use_Save_4E598BA0B88D");
            layoutEmpty.Dock = DockStyle.Bottom; layoutEmpty.Height = 46; layoutEmpty.ForeColor = Theme.Muted; list.Controls.Add(layoutEmpty);
            left.Controls.Add(list, 0, 2);
            DetailLabel(templateInfo, "guiTemplateInfo", 50); left.Controls.Add(templateInfo, 0, 3);
            defaultTemplate.Name = "defaultGuiTemplate"; defaultTemplate.Text = AOLauncher.Localization.Texts.Get("Text_Use_for_new_characters_260A66B42A63");
            defaultTemplate.AutoSize = false; defaultTemplate.Height = 28; defaultTemplate.Dock = DockStyle.Top;
            defaultTemplate.CheckedChanged += (s, e) => {
                if (updating || preview || busy || !(templates.SelectedItem is TemplateChoice choice)) return;
                try { string value = defaultTemplate.Checked ? choice.Value.Id : null; SaveDefaultTemplate?.Invoke(value); DefaultTemplateId = value;
                    Status(AOLauncher.Localization.Texts.Get("Text_Saved_for_future_characters_Existing_characters_388F61D79AFE")); }
                catch (Exception error) { Fail(error); }
                UpdateSelection();
            };
            var manage = Actions(); manage.AutoSize = false; manage.Height = 38; manage.WrapContents = false; left.Controls.Add(manage, 0, 4);
            defaultTemplate.Dock = DockStyle.None; defaultTemplate.Width = 220; defaultTemplate.Margin = new Padding(0, 4, 4, 0); manage.Controls.Add(defaultTemplate);
            var rename = ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Rename_416E84460AC8"), "renameGuiTemplate", RenameTemplate);
            var delete = ActionButton(manage, AOLauncher.Localization.Texts.Get("Text_Delete_saved_layout_30450DDD670F"), "deleteGuiTemplate", DeleteTemplate);
            selectionActions.Add(rename); selectionActions.Add(delete); rename.Visible = delete.Visible = false;
            var libraryMenu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground };
            libraryMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Rename_saved_layout_FA8ED3A28091")).Click += async (s, e) => await Safe(RenameTemplate);
            libraryMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Delete_saved_layout_233747669A92")).Click += async (s, e) => await Safe(DeleteTemplate);
            manageLayout = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Manage_C83B6AF48C1E")); manageLayout.Name = "guiManageLayout"; manage.Controls.Add(manageLayout);
            manageLayout.Click += (s, e) => libraryMenu.Show(manageLayout, new Point(0, manageLayout.Height));
            Disposed += (s, e) => libraryMenu.Dispose();
            var destination = Card(); columns.Controls.Add(destination, 1, 0);
            var right = Stack(Auto(), Fill()); destination.Controls.Add(right);
            var heading = Actions(); heading.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_2_Choose_characters_to_change_028F83B54616")));
            foreach (bool select in new[] { true, false }) {
                bool check = select;
                var link = new LinkLabel { Text = select ? AOLauncher.Localization.Texts.Get("Text_Select_all_C760CC113264") : AOLauncher.Localization.Texts.Get("Text_Clear_6EE5D5FA32F4"), AutoSize = true,
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
                    Text = AOLauncher.Localization.Texts.Get("Text_No_characters_added_yet_Add_an_3E54C5C88B92") };
                targetHost.Controls.Add(emptyTargets); emptyTargets.BringToFront();
            }
            right.Controls.Add(targetHost, 0, 1);
            var replaceCard = Card(); replaceCard.Margin = Padding.Empty; columns.Controls.Add(replaceCard, 2, 0);
            var review = Stack(Auto(), Auto(), Auto(), Fill(), Auto(), Auto()); replaceCard.Controls.Add(review);
            review.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_3_Choose_what_to_replace_033856CFB797")), 0, 0);
            review.Controls.Add(GuiComponentChoices.Create(componentChecks, "applyGuiComponent", () => { if (!updating) UpdateSelection(); }), 0, 1);
            var backupNote = Note(AOLauncher.Localization.Texts.Get("Text_A_backup_is_made_first_Restore_9714C068CD57"));
            backupNote.Name = "guiLayoutBackupNote"; backupNote.BackColor = Theme.SurfaceRaised; backupNote.Padding = new Padding(8, 6, 8, 6); backupNote.Margin = new Padding(0, 8, 0, 0);
            replaceCard.SizeChanged += (s, e) => backupNote.MaximumSize = new Size(Math.Max(100, replaceCard.ClientSize.Width - 28), 0);
            review.Controls.Add(backupNote, 0, 2);
            DetailLabel(selectedSummary, "guiTargetSummary", 44); selectedSummary.Dock = DockStyle.Bottom; review.Controls.Add(selectedSummary, 0, 4);
            var apply = Actions(); apply.WrapContents = true; review.Controls.Add(apply, 0, 5);
            applyLayout = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Review_and_apply_ECD2A4A49DD1"), "applyGuiLayout", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id), true);
            applyLayout.AutoSize = false; applyLayout.Height = 44; applyLayout.Margin = new Padding(0, 0, 0, 8);
            apply.SizeChanged += (s, e) => applyLayout.Width = Math.Max(120, apply.ClientSize.Width - 2);
            copyLayout = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Copy_from_a_character_EE01F90701DE"), "copyGuiLayout", () => CaptureLayout(true));
            applyDefault = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Use_saved_default_CEB08C10CD81"), "applyDefaultGui", () => ApplyTemplate(DefaultTemplateId));
            rolloutLayouts = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Apply_to_everyone_7ACEEB736F8C"), "rolloutAllGuiLayouts", () => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id, true));
            applyDefault.Visible = rolloutLayouts.Visible = false;
            var moreMenu = new ContextMenuStrip { BackColor = Theme.SurfaceRaised, ForeColor = Theme.Foreground };
            var savedDefault = moreMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Apply_saved_default_to_selected_characters_C1B21C11A630"));
            var applyAll = moreMenu.Items.Add(AOLauncher.Localization.Texts.Get("Text_Apply_to_all_configured_characters_0ECB8023D1AF"));
            savedDefault.Click += async (s, e) => await Safe(() => ApplyTemplate(DefaultTemplateId));
            applyAll.Click += async (s, e) => await Safe(() => ApplyTemplate((templates.SelectedItem as TemplateChoice)?.Value.Id, true));
            var more = Theme.Button(AOLauncher.Localization.Texts.Get("Text_More_actions_172B275C437C")); more.Name = "guiMoreLayoutActions"; apply.Controls.Add(more);
            more.Click += (s, e) => { savedDefault.Enabled = applyDefault.Enabled; applyAll.Enabled = rolloutLayouts.Enabled; moreMenu.Show(more, new Point(0, more.Height)); };
            Disposed += (s, e) => moreMenu.Dispose();
        }

        private void BuildSkins()
        {
            var page = Page(AOLauncher.Localization.Texts.Get("Text_Skins_A6432A47B0A5")); page.Name = "guiSkinsPage";
            var layout = Stack(Auto(), Auto(), Auto(), Fill()); page.Controls.Add(layout);
            var intro = TaskIntro(AOLauncher.Localization.Texts.Get("Text_Change_the_look_of_AO_BB26409997E9"),
                AOLauncher.Localization.Texts.Get("Text_A_skin_changes_colours_and_artwork_A805880952E8"));
            intro.Name = "guiSkinsIntro"; layout.Controls.Add(intro, 0, 0);
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            columns.RowStyles.Add(Fill()); layout.Controls.Add(columns, 0, 3);
            var targetCard = Card(); columns.Controls.Add(targetCard, 0, 0);
            var targetStack = Stack(Auto(), Auto(), Fill()); targetCard.Controls.Add(targetStack);
            var targetHeading = SectionHeading(AOLauncher.Localization.Texts.Get("Text_1_Choose_the_installation_to_change_CF9A462AA6ED"));
            targetHeading.Name = "guiSkinTargetHeading"; targetStack.Controls.Add(targetHeading, 0, 0);
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
            skinTarget.SelectedIndexChanged += (s, e) => RefreshSkins(); targetStack.Controls.Add(ComboRow(skinTarget), 0, 1);
            DetailLabel(skinReview, "guiSkinReview", 120); skinReview.Dock = DockStyle.Top; skinReview.AutoSize = true;
            targetCard.SizeChanged += (s, e) => skinReview.MaximumSize = new Size(Math.Max(100, targetCard.ClientSize.Width - 28), 0);
            targetStack.Controls.Add(skinReview, 0, 2);
            var installed = Card(); columns.Controls.Add(installed, 1, 0);
            var left = Stack(Auto(), Fill(), Auto(), Auto(), Auto()); installed.Controls.Add(left);
            left.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_2_Choose_an_installed_skin_CF6F0B80C91C")), 0, 0);
            ListStyle(skins, "guiInstalledSkins"); skins.SelectedIndexChanged += (s, e) => UpdateSelection(); left.Controls.Add(SlimScrolling.Wrap(skins), 0, 1);
            DetailLabel(skinEmpty, "guiSkinEmpty", 40); left.Controls.Add(skinEmpty, 0, 2);
            DetailLabel(skinInfo, "guiSkinInfo", 64); left.Controls.Add(skinInfo, 0, 3);
            var acquire = Card(); acquire.Margin = Padding.Empty; columns.Controls.Add(acquire, 2, 0);
            var right = Stack(Auto(), Auto(), Auto(), Auto(), Auto(), Auto(), Auto(), Auto(), Fill()); acquire.Controls.Add(right);
            right.Controls.Add(SectionHeading(AOLauncher.Localization.Texts.Get("Text_3_Apply_the_skin_FD2FA76FE2AA")), 0, 0);
            var addHeading = SectionHeading(AOLauncher.Localization.Texts.Get("Text_Add_another_skin_8DBDD1458A8D")); addHeading.Margin = new Padding(0, 18, 0, 6);
            right.Controls.Add(addHeading, 0, 2);
            right.Controls.Add(Note(AOLauncher.Localization.Texts.Get("Text_Download_or_import_first_Then_select_912AE6C7EFB3")), 0, 3);
            catalog.Name = "guiSkinCatalog"; catalog.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var entry in GuiSkins.Catalog) catalog.Items.Add(new CatalogChoice { Value = entry }); if (catalog.Items.Count > 0) catalog.SelectedIndex = 0;
            right.Controls.Add(ComboRow(catalog), 0, 4);
            clientEngine.Name = "guiSkinEngine"; clientEngine.DropDownStyle = ComboBoxStyle.DropDownList;
            clientEngine.Items.AddRange(new object[] { AOLauncher.Localization.Texts.Get("Text_Classic_client_package_90AE6744FED7"), AOLauncher.Localization.Texts.Get("Text_New_engine_package_1D4014D2B759") }); clientEngine.SelectedIndex = 0;
            right.Controls.Add(ComboRow(clientEngine), 0, 5);
            var downloads = Actions(); right.Controls.Add(downloads, 0, 6);
            skinActions.Add(ActionButton(downloads, AOLauncher.Localization.Texts.Get("Text_Download_skin_E2DA95D94775"), "downloadGuiSkin", DownloadSkin));
            skinActions.Add(ActionButton(downloads, AOLauncher.Localization.Texts.Get("Text_Import_ZIP_3DECB4D0E7FE"), "importGuiSkin", ImportSkin));
            // The acquisition column is tall at 150% scale; keep the website action visible.
            var website = Actions(); left.Controls.Add(website, 0, 4);
            ActionButton(website, AOLauncher.Localization.Texts.Get("Text_View_skin_website_F497A6485BE7"), "guiSkinWebsite", () => {
                if (catalog.SelectedItem is CatalogChoice entry && !preview) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Value.SourceUrl) { UseShellExecute = true });
                return Task.CompletedTask;
            });
            var apply = Actions(); apply.WrapContents = true; right.Controls.Add(apply, 0, 1);
            applySkin = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Review_and_apply_skin_CBB95053C033"), "applyGuiSkin", ApplySkin, true);
            defaultSkinButton = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Restore_AO_s_original_skin_D1D21EB1BD46"), "defaultGuiSkin", () => ApplySkinName("Default"));
            skinActions.Add(defaultSkinButton);
            rolloutSkins = ActionButton(apply, AOLauncher.Localization.Texts.Get("Text_Apply_to_all_197758232C05"), "rolloutAllGuiSkins", RolloutSkin);
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
                ? AOLauncher.Localization.Texts.Get("Text_No_installation_is_ready_Select_an_86B6231A9118")
                : AOLauncher.Localization.Texts.Get("Text_No_skins_found_here_Download_or_B9182DEDA0AD");
            var target = skinTarget.SelectedItem as SkinTargetChoice;
            bool alreadyActive = activeSkinName != null && string.Equals(activeSkinName, skins.SelectedItem as string, StringComparison.OrdinalIgnoreCase);
            if (applySkin != null) applySkin.Enabled = !busy && target != null && skins.SelectedItem != null && !alreadyActive;
            if (defaultSkinButton != null) defaultSkinButton.Enabled = !busy && target != null && activeSkinName != null && !string.Equals(activeSkinName, "Default", StringComparison.OrdinalIgnoreCase);
            skinReview.Text = target == null ? AOLauncher.Localization.Texts.Get("Text_Choose_an_installation_to_get_started_59E80FF52805")
                : AOLauncher.Localization.Texts.Get("Text_Applies_to_57C863CF96AC") + string.Join(", ", target.Characters.Select(c => c.Account.Label).Distinct())
                    + "\r\n" + (alreadyActive ? AOLauncher.Localization.Texts.Get("Text_This_skin_is_already_active_Choose_6591980973B2")
                        : AOLauncher.Localization.Texts.Get("Text_Nexus_saves_the_previous_skin_selection_8A27A74E0868"));
        }
    }
}
