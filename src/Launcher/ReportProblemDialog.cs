using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    /// <summary>
    /// Lets the user review an anonymised report and share it as a GitHub issue, a copied text or a file.
    /// Nothing is sent by Nexus itself; GitHub opens in the user's browser.
    /// </summary>
    internal sealed class ReportProblemDialog : EditorDialog
    {
        private static readonly ProblemArea[] Areas = { ProblemArea.Login, ProblemArea.GameGui, ProblemArea.Presets, ProblemArea.NewsHealth, ProblemArea.Updates, ProblemArea.Crash, ProblemArea.Other };
        private readonly LauncherSettings settings;
        private readonly ComboBox areaInput = new NexusComboBox { Name = "problemArea", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox summaryInput;
        private readonly TextBox descriptionInput = new NexusTextBox { Name = "problemDescription", Dock = DockStyle.Top, Multiline = true, Height = 110, ScrollBars = ScrollBars.Vertical, MaxLength = 4000, AcceptsReturn = true };
        private readonly CheckBox includeJournal = new NexusCheckBox { Name = "includeDiagnosticsLog", AutoSize = true, Dock = DockStyle.Top, Checked = true, Margin = new Padding(0, 6, 0, 4) };
        private readonly CheckBox includeCrashes = new NexusCheckBox { Name = "includeCrashReports", AutoSize = true, Dock = DockStyle.Top, Checked = true, Margin = new Padding(0, 0, 0, 8) };
        private readonly TextBox preview = new TextBox { Name = "reportPreview", Dock = DockStyle.Top, Multiline = true, ReadOnly = true, Height = 230,
            ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.FixedSingle, BackColor = LogViewer.TerminalBackground,
            ForeColor = LogViewer.TerminalForeground, Font = new Font("Consolas", 9F) };
        private readonly Timer refresh = new Timer { Interval = 400 };
        private readonly bool interactive;
        private string environment = AOLauncher.Localization.Texts.Get("Text_Collecting_environment_details_D7DF4C000818");

        internal ReportProblemDialog(LauncherSettings settings, ProblemArea area, bool interactive = true)
            : base(AOLauncher.Localization.Texts.Get("Text_Report_a_problem_45CBEF14095C"), 600)
        {
            this.settings = settings;
            this.interactive = interactive;
            // Grows to fit its content on open, within the screen's working area.
            ClientSize = new Size(760, 600);
            AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_Describe_what_went_wrong_Nexus_adds_D5A84238A255"), "reportPrivacyNote");

            Fields.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_Area_2D900204D831"), Dock = DockStyle.Top, Height = 24 });
            foreach (var item in Areas) areaInput.Items.Add(AreaLabel(item));
            areaInput.SelectedIndex = Math.Max(0, Array.IndexOf(Areas, area));
            areaInput.Margin = new Padding(0, 0, 0, 14);
            Fields.Controls.Add(areaInput);
            summaryInput = AddTextField(AOLauncher.Localization.Texts.Get("Text_Short_summary_2158DA301DDC"), "");
            summaryInput.Name = "problemSummary"; summaryInput.MaxLength = 120;
            Fields.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_What_happened_What_did_you_expect_E99F63B7B3AC"), Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            descriptionInput.Margin = new Padding(0, 0, 0, 10);
            Fields.Controls.Add(descriptionInput);
            includeJournal.Text = AOLauncher.Localization.Texts.Get("Text_Include_recent_diagnostics_log_anonymised_D12E902FA444");
            int crashes = CrashReporter.Reports().Count;
            includeCrashes.Text = crashes > 0
                ? string.Format(AOLauncher.Localization.Texts.Get("Text_Include_saved_crash_reports_0_5AF974BEAA00"), crashes)
                : AOLauncher.Localization.Texts.Get("Text_Include_saved_crash_reports_none_saved_CE07D89C6B34");
            includeCrashes.Enabled = crashes > 0; includeCrashes.Checked = crashes > 0;
            Fields.Controls.Add(includeJournal); Fields.Controls.Add(includeCrashes);
            if (!DiagnosticJournal.Enabled)
                AddWrappedNote(AOLauncher.Localization.Texts.Get("Text_The_diagnostics_log_is_turned_off_9224234DE539"), "diagnosticsDisabledNote");
            Fields.Controls.Add(new Label { Text = AOLauncher.Localization.Texts.Get("Text_Preview_of_what_will_be_shared_3A9044369C9D"), Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold), ForeColor = Theme.Accent, Margin = new Padding(0, 6, 0, 2) });
            Fields.Controls.Add(preview);

            SaveButton.Text = AOLauncher.Localization.Texts.Get("Text_Open_GitHub_issue_161E7DADC9F7"); SaveButton.Name = "openGitHubIssue";
            CancelAction.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175");
            var actions = SaveButton.Parent;
            var copy = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Copy_report_DA15267478B9")); copy.Name = "copyReport";
            var save = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Save_report_751AE73AB940")); save.Name = "saveReport";
            actions.Controls.Add(copy); actions.Controls.Add(save);
            SaveButton.Click += (sender, e) => OpenIssue();
            copy.Click += (sender, e) => { if (CopyReport(Report())) Status(AOLauncher.Localization.Texts.Get("Text_The_report_was_copied_Paste_it_3FA6E2C36B80"), false); };
            save.Click += (sender, e) => SaveReport();

            refresh.Tick += (sender, e) => { refresh.Stop(); UpdatePreview(); };
            EventHandler changed = (sender, e) => { refresh.Stop(); refresh.Start(); };
            areaInput.SelectedIndexChanged += changed; summaryInput.TextChanged += changed; descriptionInput.TextChanged += changed;
            includeJournal.CheckedChanged += changed; includeCrashes.CheckedChanged += changed;
            FormClosed += (sender, e) => refresh.Dispose();
            Shown += (sender, e) => { UpdatePreview(); _ = CollectEnvironmentAsync(); summaryInput.Focus(); };
            ActiveControl = summaryInput;
        }

        internal ProblemArea Area => Areas[Math.Max(0, areaInput.SelectedIndex)];

        internal static string AreaLabel(ProblemArea area)
        {
            switch (area)
            {
                case ProblemArea.Login: return AOLauncher.Localization.Texts.Get("Text_Login_and_characters_60A7E61E92A2");
                case ProblemArea.GameGui: return AOLauncher.Localization.Texts.Get("Text_Game_GUI_and_skins_B8CAE2F99CD4");
                case ProblemArea.Presets: return AOLauncher.Localization.Texts.Get("Text_Team_presets_and_shortcuts_4C049F7AE3E2");
                case ProblemArea.NewsHealth: return AOLauncher.Localization.Texts.Get("Text_News_and_server_health_D8E16A65062B");
                case ProblemArea.Updates: return AOLauncher.Localization.Texts.Get("Text_Updates_4FB2C3778125");
                case ProblemArea.Crash: return AOLauncher.Localization.Texts.Get("Text_Crash_or_freeze_AB184EC2F619");
                default: return AOLauncher.Localization.Texts.Get("Text_Other_22927CEA4736");
            }
        }

        private async Task CollectEnvironmentAsync()
        {
            var snapshot = settings;
            string collected;
            try { collected = await Task.Run(() => DiagnosticSnapshot.Build(snapshot, true)); }
            catch (Exception error) { collected = "Environment unavailable (" + error.GetType().Name + ")"; }
            if (IsDisposed) return;
            environment = collected;
            UpdatePreview();
        }

        internal string Report() => DiagnosticReport.Compose(Area, summaryInput.Text, descriptionInput.Text, environment,
            includeJournal.Checked && DiagnosticJournal.Enabled, includeCrashes.Checked && includeCrashes.Enabled);

        private void UpdatePreview()
        {
            if (IsDisposed) return;
            string text = Report().Replace("\r\n", "\n").Replace("\n", "\r\n");
            preview.Text = text;
        }

        private void OpenIssue()
        {
            DialogResult = DialogResult.None;
            if (string.IsNullOrWhiteSpace(summaryInput.Text) && string.IsNullOrWhiteSpace(descriptionInput.Text))
            { Status(AOLauncher.Localization.Texts.Get("Text_Add_a_short_summary_or_description_A3434C43226A"), true); summaryInput.Focus(); return; }
            string report = Report();
            bool truncated;
            string url = DiagnosticReport.BuildIssueUrl(Area, summaryInput.Text, report, out truncated);
            bool copied = CopyReport(report);
            if (!interactive) { Status(AOLauncher.Localization.Texts.Get("Text_Preview_only_GitHub_was_not_opened_B7C45C2EDB62"), false); return; }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            {
                Status(AOLauncher.Localization.Texts.Get("Text_The_browser_could_not_be_opened_A86EDF6401F8"), true);
                return;
            }
            DiagnosticJournal.Record("report", "Opened a GitHub issue draft for area " + DiagnosticReport.AreaName(Area) + (truncated ? " (report shortened in the link)" : ""));
            CrashReporter.AcknowledgePending();
            Status(truncated && copied
                ? AOLauncher.Localization.Texts.Get("Text_GitHub_opened_in_your_browser_The_0AE335CDB1E9")
                : AOLauncher.Localization.Texts.Get("Text_GitHub_opened_in_your_browser_Review_6BE998C90043"), false);
        }

        private bool CopyReport(string report)
        {
            try { Clipboard.SetText(report); return true; }
            catch (ExternalException) { Status(AOLauncher.Localization.Texts.Get("Text_The_clipboard_is_busy_Try_again_67B8D7E989EA"), true); return false; }
            catch (System.Threading.ThreadStateException) { return false; }
        }

        private void SaveReport()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = AOLauncher.Localization.Texts.Get("Text_Save_report_C539DE9BC0F4"),
                Filter = AOLauncher.Localization.Texts.Get("Text_Markdown_report_md_md_Text_file_AB094FF81D0F"),
                FileName = "AO-Nexus-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".md",
                OverwritePrompt = true, AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, Report(), new UTF8Encoding(false));
                    CrashReporter.AcknowledgePending();
                    Status(AOLauncher.Localization.Texts.Get("Text_Report_saved_You_can_attach_the_CD047DF93C65"), false);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
                { Status(AOLauncher.Localization.Texts.Get("Text_The_report_could_not_be_saved_0B473E9DBE74") + error.Message, true); }
            }
        }

        private void Status(string message, bool error)
        {
            ErrorLabel.Text = message;
            ErrorLabel.ForeColor = error ? Theme.Warning : Theme.Accent;
        }
    }
}
