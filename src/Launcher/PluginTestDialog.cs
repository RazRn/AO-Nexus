using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    // Plugins page > Test plugins: loads one target into a running character at a time and
    // asks Nexus Shortcuts which of them started (see PluginHealth). Afterwards the character's own
    // set is loaded again. The result per plugin is kept and shown as a dot in the plugin list.
    internal sealed class PluginTestDialog : EditorDialog
    {
        const int MinimumWaitSeconds = 8, MaximumWaitSeconds = 18;

        readonly List<PluginAutoInjector.Row> clients;
        readonly List<PluginInfo> catalog;
        readonly LauncherSettings working;
        readonly ComboBox character, scope;
        readonly CheckBox includeRisky;
        readonly Label progress;
        readonly DataGridView results;
        bool runningTest, stopRequested;

        internal Func<PluginAutoInjector.Row, string[], string, PluginHostClient.Response> SendPlugins { private get; set; }
        internal Action<PluginAutoInjector.Row, bool> SetTesting { private get; set; }
        internal Func<PluginAutoInjector.Row, LauncherSettings, string> LoadNow { private get; set; }
        internal Func<PluginAutoInjector.Row, string> UnloadNow { private get; set; }
        internal Action<List<PluginTestRecord>> SaveResults { private get; set; }
        internal Func<int, Tuple<string, string>> ReadCharacter = ShortcutClient.Who;
        internal Func<PluginAutoInjector.Row, string[], Func<string, string>, Task<List<PluginCheck>>> ObserveStartup;

        sealed class Scope { internal string Text; internal Func<PluginInfo, bool> Includes; public override string ToString() => Text; }

        internal PluginTestDialog(List<PluginAutoInjector.Row> loaded, List<PluginInfo> catalog, LauncherSettings working, PluginAutoInjector.Row preferred)
            : base(AOLauncher.Localization.Texts.Get("Text_Test_plugins_BF75F62EFB94"), 640)
        {
            clients = loaded; this.catalog = catalog; this.working = working;
            ClientSize = new Size(820, 640);
            FitContentOnOpen = false;
            DialogLayout.Controls.Remove(FieldsViewport);
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Background, Margin = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            DialogLayout.Controls.Add(stack, 0, 0);

            stack.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Theme.Foreground, Margin = new Padding(0, 0, 0, 10), UseMnemonic = false,
                Text = AOLauncher.Localization.Texts.Get("Text_Startup_test_only_one_plugin_at_D231AB53C3D9")
            }, 0, 0);

            var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, BackColor = Theme.Background, Margin = new Padding(0, 0, 0, 6) };
            options.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_Character_C8A1DC62F40D"), AutoSize = true, Margin = new Padding(0, 8, 6, 0), ForeColor = Theme.Muted });
            character = new NexusComboBox { Name = "testCharacter", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Margin = new Padding(0, 3, 16, 0) };
            foreach (var row in clients) character.Items.Add(row.CharacterName);
            character.SelectedIndex = clients.Count == 0 ? -1 : Math.Max(0, preferred == null ? 0 : clients.IndexOf(preferred));
            options.Controls.Add(character);
            options.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_Plugins_C62A56D7ECF3"), AutoSize = true, Margin = new Padding(0, 8, 6, 0), ForeColor = Theme.Muted });
            scope = new NexusComboBox { Name = "testScope", DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, Margin = new Padding(0, 3, 16, 0) };
            var scopes = new List<Scope>
            {
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_Core_candidates_startup_only_A9FA097D70F1"), Includes = plugin => PluginTestPolicy.Entries.Any(entry => entry.Id == plugin.Id && entry.Class == "Candidate") },
                new Scope { Text = string.Format(AOLauncher.Localization.Texts.Get("Text_All_in_the_pack_and_yours_BC89E24FB6CB"), Testable().Count()), Includes = plugin => true },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_Z_suite_9B76BB30B2AB"), Includes = plugin => plugin.Suite == "Z" },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_AOSP_suite_EC1EE5325193"), Includes = plugin => plugin.Suite == "AOSP" },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_Classic_plugins_941E791A4CA0"), Includes = plugin => plugin.Suite == "Classic" },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_Your_own_DLLs_53B369D9A5D0"), Includes = plugin => plugin.Custom },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_No_matching_startup_result_B3AC3E0BBCE7"), Includes = plugin => CurrentTest(plugin) == null },
                new Scope { Text = AOLauncher.Localization.Texts.Get("Text_Failed_last_time_E5F1EF9F5544"), Includes = plugin => CurrentTest(plugin) is PluginTestRecord test && test.Status != nameof(PluginStatus.Running) && test.Status != nameof(PluginStatus.Started) },
            };
            foreach (var item in scopes) scope.Items.Add(item);
            scope.SelectedIndex = 0;
            options.Controls.Add(scope);
            options.SetFlowBreak(scope, true);
            includeRisky = new NexusCheckBox { Name = "testScenarios", AutoSize = false, Size = new Size(340, 26), Margin = new Padding(0, 7, 0, 0),
                Text = AOLauncher.Localization.Texts.Get("Text_Include_specialised_plugins_10160172A375") };
            options.Controls.Add(includeRisky);
            stack.Controls.Add(options, 0, 1);

            progress = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 4, 0, 6), UseMnemonic = false,
                Text = AOLauncher.Localization.Texts.Get("Text_Results_describe_startup_only_Excluded_plugins_4B9BA842CD1E") };
            stack.Controls.Add(progress, 0, 2);

            results = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Surface, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            results.Columns.Add("Plugin", AOLauncher.Localization.Texts.Get("Text_Plugin_D90658A9FB42")); results.Columns.Add("Result", AOLauncher.Localization.Texts.Get("Text_Result_B85A3D9347A7")); results.Columns.Add("Details", AOLauncher.Localization.Texts.Get("Text_Details_8D46C71D0B8C"));
            results.Columns[0].FillWeight = 28; results.Columns[1].FillWeight = 22; results.Columns[2].FillWeight = 50;
            stack.Controls.Add(SlimScrolling.Wrap(results), 0, 3);

            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Start_test_427D3B0B33E8");
            SaveButton.Enabled = clients.Count > 0;
            SaveButton.Click += async (sender, e) => { if (runningTest) Stop(); else await RunAsync(); };
            CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175");
            AcceptButton = null;
            FormClosing += (sender, e) =>
            {
                if (!runningTest) return;
                e.Cancel = true; Stop();
                ErrorLabel.ForeColor = Theme.Amber; ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Stopping_the_character_s_own_set_1AD3E6C57493");
            };
            if (clients.Count == 0) { ErrorLabel.ForeColor = Theme.Amber; ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_Load_plugins_into_a_character_first_E61F8806B08A"); }
        }

        IEnumerable<PluginInfo> Testable() => catalog.Where(plugin => plugin.Id != PluginSet.ShortcutsId && !string.IsNullOrEmpty(plugin.Path));

        PluginTestRecord CurrentTest(PluginInfo plugin)
        {
            var record = working.PluginTests.LastOrDefault(item => item.PluginId == plugin.Id);
            if (record?.TestKind != "Startup") return null;
            try { return PluginTestPolicy.ResultMatches(record, PluginTestPolicy.FileHash(plugin.Path)) ? record : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        void Stop()
        {
            stopRequested = true;
            SaveButton.Enabled = false;
            progress.Text = AOLauncher.Localization.Texts.Get("Text_Stopping_after_this_plugin_DC5853C598DF");
        }

        // One target beside the instrumentation. No batches of interacting bots.
        internal static List<List<PluginInfo>> Batches(IEnumerable<PluginInfo> plugins)
            => plugins.OrderBy(plugin => plugin.Suite).ThenBy(plugin => plugin.Name)
                .Select(plugin => new List<PluginInfo> { plugin }).ToList();

        async Task RunAsync()
        {
            var row = clients[character.SelectedIndex];
            var shortcuts = catalog.FirstOrDefault(plugin => plugin.Id == PluginSet.ShortcutsId);
            if (shortcuts == null || !PluginTestPolicy.InstrumentMatches(shortcuts))
            {
                ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_The_verified_Nexus_Shortcuts_build_is_9CC4FF630904");
                return;
            }
            var chosen = ((Scope)scope.SelectedItem).Includes;
            var plugins = Testable().Where(chosen).ToList();
            if (plugins.Count == 0) { ErrorLabel.ForeColor = Theme.Amber; ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_No_plugins_to_test_with_these_3DCCCD1D7E49"); return; }
            var names = catalog.Where(plugin => !string.IsNullOrEmpty(plugin.Path)).GroupBy(plugin => plugin.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
            var byPath = catalog.Where(plugin => !string.IsNullOrEmpty(plugin.Path)).GroupBy(plugin => plugin.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            runningTest = true; stopRequested = false;
            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Stop_54ED8C868A9E");
            character.Enabled = scope.Enabled = includeRisky.Enabled = false;
            ErrorLabel.Text = "";
            results.Rows.Clear();
            var records = new List<PluginTestRecord>();
            int done = 0, skipped = 0;
            string profession = null;
            bool modifiedClient = false;
            string abort = null;
            SetTesting?.Invoke(row, true);
            try
            {
                var who = await Task.Run(() => ReadCharacter(row.Pid));
                if (who == null || !string.Equals(who.Item2, row.CharacterName, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(AOLauncher.Localization.Texts.Get("Text_The_selected_character_could_not_be_AF726AEFE473"));
                profession = who.Item1;
                var eligible = new List<PluginInfo>();
                foreach (var plugin in plugins) {
                    string reason = PluginTestPolicy.Rejection(plugin, includeRisky.Checked, profession, out _);
                    if (reason == null) eligible.Add(plugin);
                    else { skipped++; AddResult(plugin.Name, AOLauncher.Localization.Texts.Get("Text_Skipped_6F2C7F0E0C89"), reason, Theme.Muted); }
                }
                plugins = eligible;
                var batches = Batches(plugins);
                for (int index = 0; index < batches.Count && !stopRequested && abort == null; index++)
                {
                    var batch = batches[index];
                    string reason = PluginTestPolicy.Rejection(batch[0], includeRisky.Checked, profession, out string testedHash);
                    if (reason != null) { abort = reason; break; }
                    progress.Text = string.Format(AOLauncher.Localization.Texts.Get("Text_Batch_0_of_1_2_06F50AC5B0D1"), index + 1, batches.Count, string.Join(", ", batch.Select(plugin => plugin.Name)));
                    var paths = new[] { shortcuts.Path }.Concat(batch.Select(plugin => plugin.Path)).ToArray();
                    if (!PluginTestPolicy.InstrumentMatches(shortcuts)) throw new IOException(AOLauncher.Localization.Texts.Get("Text_The_test_instrumentation_changed_Restart_with_635A5039801B"));
                    modifiedClient = true;
                    var response = await Task.Run(() => SendPlugins(row, paths, "plugin test " + (index + 1) + "/" + batches.Count));
                    if (response == null || !response.Ok)
                    {
                        abort = response?.Error ?? AOLauncher.Localization.Texts.Get("Text_The_plugin_host_did_not_answer_C351ADB48978");
                        break;
                    }
                    var checks = await (ObserveStartup ?? WaitForHealth)(row, batch.Select(plugin => plugin.Path).ToArray(), path => names.TryGetValue(path, out string name) ? name : null);
                    if (checks == null)
                    {
                        foreach (var plugin in batch) AddResult(plugin.Name, AOLauncher.Localization.Texts.Get("Text_No_answer_495B579E6CB9"), AOLauncher.Localization.Texts.Get("Text_Nexus_Shortcuts_did_not_answer_the_9CF833F4792B"), Theme.Muted);
                        abort = AOLauncher.Localization.Texts.Get("Text_The_game_stopped_answering_2A39BDCA3FDB");
                        break;
                    }
                    if (PluginTestPolicy.FileHash(batch[0].Path) != testedHash) {
                        abort = AOLauncher.Localization.Texts.Get("Text_The_plugin_file_changed_during_the_00BF1E4EA1E5"); break;
                    }
                    foreach (var check in checks)
                    {
                        AddResult(check.Name, check.StatusText(), check.Message ?? (check.Commands.Count > 0 ? "/" + string.Join(" /", check.Commands) : ""), PluginGrid.TestColor(check.Status.ToString()));
                        if (byPath.TryGetValue(check.Path, out var plugin))
                            records.Add(new PluginTestRecord { PluginId = plugin.Id, Status = check.Status.ToString(), Message = check.Message, Character = row.CharacterName, TestedUtcTicks = DateTime.UtcNow.Ticks,
                                TestKind = "Startup", PluginSha256 = testedHash, Profession = profession,
                                ServerEndpoint = working.Accounts.FirstOrDefault(account => account.Id == row.AccountId) is AccountConfig target ? AccountTargets.Endpoint(target) : null });
                    }
                    done += batch.Count;
                    if (checks.Any(check => check.Problem)) abort = AOLauncher.Localization.Texts.Get("Text_A_plugin_failed_its_startup_check_96AD7A2B600A");
                }
            }
            catch (Exception ex) { abort = ex.Message; }
            finally
            {
                progress.Text = AOLauncher.Localization.Texts.Get("Text_Loading_the_character_s_own_plugin_0491B5A5C5AD");
                string restoreError = null;
                if (modifiedClient) try { restoreError = await Task.Run(() => LoadNow?.Invoke(row, working) ?? (LoadNow == null ? "Plugin restore is unavailable." : null)); }
                catch (Exception ex) { restoreError = ex.Message; }
                string unloadError = null;
                if (restoreError != null)
                {
                    try { unloadError = await Task.Run(() => UnloadNow?.Invoke(row) ?? (UnloadNow == null ? "Plugin unload is unavailable." : null)); }
                    catch (Exception ex) { unloadError = ex.Message; }
                }
                SetTesting?.Invoke(row, false);
                runningTest = false;
                if (records.Count > 0) SaveResults?.Invoke(records);
                SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Start_test_427D3B0B33E8"); SaveButton.Enabled = true;
                character.Enabled = scope.Enabled = includeRisky.Enabled = true;
                int good = records.Count(record => record.Status == nameof(PluginStatus.Running) || record.Status == nameof(PluginStatus.Started));
                progress.Text = string.Format(AOLauncher.Localization.Texts.Get("Text_0_of_1_startup_checks_2_79A0F30FC3F6"), done, plugins.Count, good, records.Count - good, skipped)
                    + (restoreError != null ? " " + (unloadError == null
                        ? AOLauncher.Localization.Texts.Get("Text_The_original_set_could_not_be_A8D74FFC8DBF") + restoreError
                        : AOLauncher.Localization.Texts.Get("Text_Test_plugins_could_not_be_removed_2516EA2F2393") + unloadError) : "");
                if (abort != null) { ErrorLabel.ForeColor = Theme.Warning; ErrorLabel.Text = AOLauncher.Localization.Texts.Get("Text_The_test_stopped_A8ACFC5FD03B") + abort; }
            }
        }

        // Waits for the batch to load and start, then a little longer for errors while running.
        async Task<List<PluginCheck>> WaitForHealth(PluginAutoInjector.Row row, string[] paths, Func<string, string> nameOf)
        {
            var started = DateTime.UtcNow;
            List<PluginCheck> checks = null;
            while ((DateTime.UtcNow - started).TotalSeconds < MaximumWaitSeconds)
            {
                await Task.Delay(2000);
                var report = await Task.Run(() => ShortcutClient.Health(row.Pid));
                if (report == null) continue;
                var current = PluginHealth.Evaluate(paths, report, nameOf);
                // A report from before the reload knows none of this batch's files.
                if (current.All(check => check.Status == PluginStatus.NotLoaded) && (DateTime.UtcNow - started).TotalSeconds < MaximumWaitSeconds - 4) continue;
                checks = current;
                bool settled = checks.All(check => check.Status != PluginStatus.Started);
                if ((DateTime.UtcNow - started).TotalSeconds >= MinimumWaitSeconds && settled) break;
            }
            return checks;
        }

        void AddResult(string plugin, string result, string details, Color color)
        {
            int index = results.Rows.Add(plugin, result, details);
            results.Rows[index].Cells[1].Style.ForeColor = color;
            results.Rows[index].Cells[2].ToolTipText = details;
            results.FirstDisplayedScrollingRowIndex = index;
        }
    }
}
