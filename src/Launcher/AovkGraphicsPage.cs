using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class AovkGraphicsPage
    {
        private readonly Dictionary<string, AovkGraphics.Snapshot> drafts = new Dictionary<string, AovkGraphics.Snapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CheckBox> toggles = new Dictionary<string, CheckBox>();
        private readonly ComboBox folders, filtering;
        private readonly CheckBox enabled;
        private readonly Label status;
        private readonly Button preset, recover;
        private readonly TableLayoutPanel effects;
        private AovkGraphics.Snapshot current;
        private bool loading;

        internal Func<bool> ExperimentEnabled { get; set; }
        internal void RefreshAvailability() => UpdateEnabled();
        void EnsureExperiment()
        {
            if (HasChanges && ExperimentEnabled?.Invoke() != true)
                throw new IOException(AOLauncher.Localization.Texts.Get("Experiments.EnableAovk"));
        }
        internal AovkGraphicsPage(TableLayoutPanel fields, LauncherSettings settings, string preferredFolder = null)
        {
            ExperimentEnabled = () => settings.ExperimentalAovk;
            Note(fields, AOLauncher.Localization.Texts.Get("Experiments.EnableAovk"));
            Note(fields, AOLauncher.Localization.Texts.Get("Text_Choose_a_game_installation_Changes_apply_1F9B5F786BCB"));
            folders = new NexusComboBox { Name = "graphicsGameFolder", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, MinimumSize = new Size(0, 28) };
            var folderRow = new Panel { Dock = DockStyle.Top, Height = 34, MinimumSize = new Size(0, 34), Margin = Padding.Empty };
            folderRow.Controls.Add(folders);
            fields.Controls.Add(folderRow);
            // Reserve the native selector's row even before its handle exists.
            // Auto-sized tables can otherwise measure a docked ComboBox as zero.
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            var browse = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Choose_another_game_folder_6A6DBAACA91D"));
            browse.Name = "graphicsBrowse"; fields.Controls.Add(browse);
            browse.Click += (sender, e) => {
                using (var picker = new FolderBrowserDialog { Description = AOLauncher.Localization.Texts.Get("Text_Choose_the_folder_containing_AnarchyOnline_exe_6DA48586C418"), ShowNewFolderButton = false }) {
                    if (picker.ShowDialog(fields.FindForm()) != DialogResult.OK) return;
                    string path = AovkGraphics.NormalizeFolder(picker.SelectedPath);
                    if (!folders.Items.Contains(path)) folders.Items.Add(path);
                    folders.SelectedItem = path;
                }
            };
            status = Note(fields, ""); status.Name = "graphicsStatus";
            recover = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Restore_interrupted_graphics_change_AFBECE6E1D76"));
            recover.Name = "aovkRecover"; recover.Visible = false; fields.Controls.Add(recover);
            recover.Click += (sender, e) => {
                try {
                    if (ChangeBlocked?.Invoke() == true) throw new IOException(AOLauncher.Localization.Texts.Get("Text_Wait_for_the_current_game_operation_3F7564546B13"));
                    string path = folders.SelectedItem as string;
                    AovkGraphics.Recover(path); drafts.Remove(path); SelectFolder();
                }
                catch (Exception ex) { status.ForeColor = Theme.Warning; status.Text = ex.Message; }
            };
            enabled = new NexusCheckBox { Name = "aovkEnabled", Text = AOLauncher.Localization.Texts.Get("Text_Use_AO_VK_A1916EC93BF2"), AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 8) };
            fields.Controls.Add(enabled);
            Note(fields, AOLauncher.Localization.Texts.Get("Text_Enable_and_Save_to_download_AO_DE6F52555714"));
            effects = new TableLayoutPanel { Name = "aovkEffects", ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top, Margin = Padding.Empty };
            effects.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); fields.Controls.Add(effects);
            AddToggle("RVK_NexusUI", AOLauncher.Localization.Texts.Get("Text_Modern_plugin_windows_SyncManager_preview_75937341AB60"));
            Note(effects, AOLauncher.Localization.Texts.Get("Text_Optional_test_renderer_Open_syncmanager_in_B395E26F3740"));
            AddToggle("RVK_Enhance", AOLauncher.Localization.Texts.Get("Text_Enhanced_graphics_1A29FFD838D1"));
            AddToggle("RVK_PixelLight", AOLauncher.Localization.Texts.Get("Text_Per_pixel_lighting_2C5E24FA74BA"));
            AddToggle("RVK_LightOver", AOLauncher.Localization.Texts.Get("Text_Nearby_lights_A4FC94799845"));
            AddToggle("RVK_SunShadow", AOLauncher.Localization.Texts.Get("Text_Sun_shadows_8AA949BB8052"));
            AddToggle("RVK_ContactOn", AOLauncher.Localization.Texts.Get("Text_Contact_shadows_1BC02DEE1314"));
            AddToggle("RVK_PtOn", AOLauncher.Localization.Texts.Get("Text_Shadows_from_nearby_lights_774FCF624739"));
            AddToggle("RVK_Hdr", AOLauncher.Localization.Texts.Get("Text_HDR_lighting_D618041F620B"));
            AddToggle("RVK_BumpOn", AOLauncher.Localization.Texts.Get("Text_Generated_surface_detail_bump_mapping_8F5DB880D3F3"));
            Note(effects, AOLauncher.Localization.Texts.Get("Text_Texture_filtering_68C4C497D5BE"));
            filtering = new NexusComboBox { Name = "aovkFiltering", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, MinimumSize = new Size(0, 28), Margin = new Padding(0, 0, 0, 8) };
            filtering.Items.AddRange(new object[] { AOLauncher.Localization.Texts.Get("Text_Off_CAD65C6B8619"), "2×", "4×", "8×", "16×" }); effects.Controls.Add(filtering);
            AddToggle("RVK_PerfHud", AOLauncher.Localization.Texts.Get("Text_Show_performance_overlay_FA7752F25B8D"));
            preset = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Use_tested_lighting_preset_5977D792A2F3")); preset.Name = "aovkTestedPreset";
            preset.Margin = new Padding(0, 8, 0, 8); effects.Controls.Add(preset);
            Note(fields, AOLauncher.Localization.Texts.Get("Text_These_controls_come_from_the_configurations_F7D1F433735D"));
            Note(fields, AOLauncher.Localization.Texts.Get("Text_Under_investigation_frame_generation_TAA_sky_7A488D4E5B38"));
            enabled.CheckedChanged += (sender, e) => { if (!loading && current != null) { current.Enabled = enabled.Checked; UpdateEnabled(); } };
            filtering.SelectedIndexChanged += (sender, e) => {
                if (!loading && current != null && filtering.SelectedIndex >= 0)
                    current.Values["RVK_Aniso"] = new[] { "1", "2", "4", "8", "16" }[filtering.SelectedIndex];
            };
            preset.Click += (sender, e) => {
                if (current == null) return;
                current.Preset = true;
                foreach (var key in AovkGraphics.Controls.Where(key => key != "RVK_NexusUI")) current.Values[key] = key == "RVK_Aniso" ? "16" : "1";
                ShowCurrent();
                status.Text = AOLauncher.Localization.Texts.Get("Text_Tested_lighting_preset_selected_Save_to_040C396495EA");
            };
            folders.SelectedIndexChanged += (sender, e) => SelectFolder();
            var paths = new[] { preferredFolder }.Concat(settings.Accounts.Select(account => account.GamePath)).Concat(new[] { settings.GamePath })
                .Where(path => !string.IsNullOrWhiteSpace(path));
            foreach (var path in paths)
            {
                try { var normalized = AovkGraphics.NormalizeFolder(path); if (!folders.Items.Contains(normalized)) folders.Items.Add(normalized); }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            if (folders.Items.Count > 0) folders.SelectedIndex = 0;
            else { status.Text = AOLauncher.Localization.Texts.Get("Text_Add_an_account_with_a_game_EDC0AB01D21D"); UpdateEnabled(); }
        }

        private void AddToggle(string key, string text)
        {
            var check = new NexusCheckBox { Name = key, Text = text, AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
            toggles.Add(key, check); effects.Controls.Add(check);
            check.CheckedChanged += (sender, e) => {
                if (!loading && current != null) { current.Values[key] = check.Checked ? "1" : "0"; UpdateEnabled(); }
            };
        }
        private void SelectFolder()
        {
            current = null;
            recover.Visible = false;
            try
            {
                var path = folders.SelectedItem as string;
                if (path == null) return;
                if (!drafts.TryGetValue(path, out current)) { current = AovkGraphics.Read(path); drafts.Add(path, current); }
                status.ForeColor = Theme.Muted;
                status.Text = current.ProfileName + " · AO-VK 0.7.1\r\n" + (current.Enabled
                    ? (current.UpgradeAvailable ? AOLauncher.Localization.Texts.Get("Text_Window_fix_available_Close_the_game_2F7E8BF78B5F") : AOLauncher.Localization.Texts.Get("Text_AO_VK_is_active_D87B923A0ACA"))
                    : AOLauncher.Localization.Texts.Get("Text_Standard_graphics_Enable_AO_VK_and_8A7FB2330358"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            { current = null; status.ForeColor = Theme.Warning; status.Text = ex.Message;
                var path = folders.SelectedItem as string; recover.Visible = path != null && AovkGraphics.HasPending(path); }
            finally { ShowCurrent(); }
        }
        private void ShowCurrent()
        {
            loading = true;
            enabled.Checked = current != null && current.Enabled;
            foreach (var pair in toggles) pair.Value.Checked = current != null && current.Values[pair.Key] == "1";
            filtering.SelectedIndex = current == null ? 0 : Array.IndexOf(new[] { "1", "2", "4", "8", "16" }, current.Values["RVK_Aniso"]);
            loading = false; UpdateEnabled();
        }
        private void UpdateEnabled()
        {
            enabled.Enabled = current != null && ExperimentEnabled?.Invoke() == true;
            effects.Enabled = current != null && enabled.Checked && ExperimentEnabled?.Invoke() == true;
            preset.Enabled = enabled.Enabled;
            foreach (var pair in toggles) pair.Value.Enabled = pair.Key == "RVK_Enhance" || pair.Key == "RVK_PerfHud" || pair.Key == "RVK_NexusUI" || toggles["RVK_Enhance"].Checked;
            filtering.Enabled = toggles["RVK_Enhance"].Checked;
        }
        internal bool HasChanges => drafts.Values.Any(draft => draft.Changed);
        internal bool PrepareDownload(IWin32Window owner)
        {
            EnsureExperiment();
            if (!drafts.Values.Any(draft => draft.Changed && draft.Enabled)) return true;
            foreach (var draft in drafts.Values.Where(draft => draft.Changed)) AovkGraphics.EnsureClosed(draft.Folder);
            foreach (var draft in drafts.Values.Where(draft => draft.Changed && draft.Enabled && draft.Rebirth)) AovkGraphics.RebirthPayload(draft);
            if (!drafts.Values.Any(draft => draft.Changed && AovkGraphics.NeedsRendererDownload(draft))) return true;
            AovkGraphics.DownloadedPayload = OptionalComponents.ReadAovkFile;
            if (AovkGraphics.HasEmbeddedPayload) return true;
            string downloaded = ComponentDownloadDialog.Download(owner, "aovk");
            if (downloaded == null) return false;
            AovkGraphics.DownloadedPayload = name => File.ReadAllBytes(Path.Combine(downloaded, name));
            return true;
        }
        internal Func<bool> ChangeBlocked { get; set; }
        internal void Save() { EnsureExperiment(); AovkGraphics.Apply(drafts.Values); }
        private static Label Note(TableLayoutPanel fields, string text)
        {
            var note = new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(500, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 8) };
            fields.Controls.Add(note);
            fields.ClientSizeChanged += (sender, e) => note.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 24), 0);
            return note;
        }
    }
}
