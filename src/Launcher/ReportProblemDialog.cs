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
        private string environment = L("Collecting environment details…", "Indsamler oplysninger om systemet…");

        internal ReportProblemDialog(LauncherSettings settings, ProblemArea area, bool interactive = true)
            : base(L("Report a problem", "Rapportér et problem"), 600)
        {
            this.settings = settings;
            this.interactive = interactive;
            // Grows to fit its content on open, within the screen's working area.
            ClientSize = new Size(760, 600);
            AddWrappedNote(L("Describe what went wrong. Nexus adds an anonymised report: versions, Windows, a summary of your setup and recent diagnostics. Logins, account and character names, character IDs, team names, custom server addresses and personal folder names are replaced with codes such as Char-3fa2c1. Passwords are never logged. Review the preview before sharing; nothing is sent automatically.",
                "Beskriv hvad der gik galt. Nexus tilføjer en anonymiseret rapport: versioner, Windows, et overblik over din opsætning og seneste diagnostik. Logins, konto- og char-navne, char-ID'er, holdnavne, egne serveradresser og personlige mappenavne erstattes af koder som Char-3fa2c1. Adgangskoder logges aldrig. Gennemse forhåndsvisningen før du deler; intet sendes automatisk."), "reportPrivacyNote");

            Fields.Controls.Add(new Label { Text = L("Area", "Område"), Dock = DockStyle.Top, Height = 24 });
            foreach (var item in Areas) areaInput.Items.Add(AreaLabel(item));
            areaInput.SelectedIndex = Math.Max(0, Array.IndexOf(Areas, area));
            areaInput.Margin = new Padding(0, 0, 0, 14);
            Fields.Controls.Add(areaInput);
            summaryInput = AddTextField(L("Short summary", "Kort resumé"), "");
            summaryInput.Name = "problemSummary"; summaryInput.MaxLength = 120;
            Fields.Controls.Add(new Label { Text = L("What happened? What did you expect? Steps to reproduce help a lot.", "Hvad skete der? Hvad forventede du? Trin til at genskabe fejlen hjælper meget."), Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            descriptionInput.Margin = new Padding(0, 0, 0, 10);
            Fields.Controls.Add(descriptionInput);
            includeJournal.Text = L("Include recent diagnostics log (anonymised)", "Medtag seneste diagnostiklog (anonymiseret)");
            int crashes = CrashReporter.Reports().Count;
            includeCrashes.Text = crashes > 0
                ? string.Format(L("Include saved crash reports ({0})", "Medtag gemte nedbrudsrapporter ({0})"), crashes)
                : L("Include saved crash reports (none saved)", "Medtag gemte nedbrudsrapporter (ingen gemt)");
            includeCrashes.Enabled = crashes > 0; includeCrashes.Checked = crashes > 0;
            Fields.Controls.Add(includeJournal); Fields.Controls.Add(includeCrashes);
            if (!DiagnosticJournal.Enabled)
                AddWrappedNote(L("The diagnostics log is turned off in Settings → Updates, so only the environment summary can be included.",
                    "Diagnostikloggen er slået fra under Indstillinger → Opdateringer, så kun systemoverblikket kan medtages."), "diagnosticsDisabledNote");
            Fields.Controls.Add(new Label { Text = L("Preview of what will be shared", "Forhåndsvisning af det, der deles"), Dock = DockStyle.Top, Height = 24, Font = new Font(Font, FontStyle.Bold), ForeColor = Theme.Accent, Margin = new Padding(0, 6, 0, 2) });
            Fields.Controls.Add(preview);

            SaveButton.Text = L("Open GitHub issue…", "Åbn GitHub-sag…"); SaveButton.Name = "openGitHubIssue";
            CancelAction.Text = L("Close", "Luk");
            var actions = SaveButton.Parent;
            var copy = Theme.Button(L("Copy report", "Kopiér rapport")); copy.Name = "copyReport";
            var save = Theme.Button(L("Save report…", "Gem rapport…")); save.Name = "saveReport";
            actions.Controls.Add(copy); actions.Controls.Add(save);
            SaveButton.Click += (sender, e) => OpenIssue();
            copy.Click += (sender, e) => { if (CopyReport(Report())) Status(L("The report was copied. Paste it into a GitHub issue, Discord or an e-mail.", "Rapporten er kopieret. Indsæt den i en GitHub-sag, på Discord eller i en e-mail."), false); };
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
                case ProblemArea.Login: return L("Login and characters", "Login og chars");
                case ProblemArea.GameGui: return L("Game GUI and skins", "Spil-GUI og skins");
                case ProblemArea.Presets: return L("Team presets and shortcuts", "Hold og genveje");
                case ProblemArea.NewsHealth: return L("News and server health", "Nyheder og serverstatus");
                case ProblemArea.Updates: return L("Updates", "Opdateringer");
                case ProblemArea.Crash: return L("Crash or freeze", "Nedbrud eller frys");
                default: return L("Other", "Andet");
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
            { Status(L("Add a short summary or description first.", "Tilføj først et kort resumé eller en beskrivelse."), true); summaryInput.Focus(); return; }
            string report = Report();
            bool truncated;
            string url = DiagnosticReport.BuildIssueUrl(Area, summaryInput.Text, report, out truncated);
            bool copied = CopyReport(report);
            if (!interactive) { Status(L("Preview only: GitHub was not opened.", "Kun forhåndsvisning: GitHub blev ikke åbnet."), false); return; }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            {
                Status(L("The browser could not be opened. Use Copy report or Save report and share it another way.", "Browseren kunne ikke åbnes. Brug Kopiér rapport eller Gem rapport og del den på anden vis."), true);
                return;
            }
            DiagnosticJournal.Record("report", "Opened a GitHub issue draft for area " + DiagnosticReport.AreaName(Area) + (truncated ? " (report shortened in the link)" : ""));
            CrashReporter.AcknowledgePending();
            Status(truncated && copied
                ? L("GitHub opened in your browser. The report was too long for the link, so the full version is on your clipboard: paste it into the Diagnostics box before submitting.",
                    "GitHub er åbnet i din browser. Rapporten var for lang til linket, så den fulde version ligger i udklipsholderen: indsæt den i feltet Diagnostics før du sender.")
                : L("GitHub opened in your browser. Review the issue and choose Submit new issue. A GitHub account is required; otherwise use Copy report.",
                    "GitHub er åbnet i din browser. Gennemse sagen og vælg Submit new issue. Det kræver en GitHub-konto; ellers brug Kopiér rapport."), false);
        }

        private bool CopyReport(string report)
        {
            try { Clipboard.SetText(report); return true; }
            catch (ExternalException) { Status(L("The clipboard is busy. Try again, or use Save report.", "Udklipsholderen er optaget. Prøv igen, eller brug Gem rapport."), true); return false; }
            catch (System.Threading.ThreadStateException) { return false; }
        }

        private void SaveReport()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = L("Save report", "Gem rapport"),
                Filter = L("Markdown report (*.md)|*.md|Text file (*.txt)|*.txt", "Markdown-rapport (*.md)|*.md|Tekstfil (*.txt)|*.txt"),
                FileName = "AO-Nexus-report-" + DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture) + ".md",
                OverwritePrompt = true, AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, Report(), new UTF8Encoding(false));
                    CrashReporter.AcknowledgePending();
                    Status(L("Report saved. You can attach the file to a GitHub issue or send it to the developer.", "Rapporten er gemt. Du kan vedhæfte filen til en GitHub-sag eller sende den til udvikleren."), false);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
                { Status(L("The report could not be saved: ", "Rapporten kunne ikke gemmes: ") + error.Message, true); }
            }
        }

        private void Status(string message, bool error)
        {
            ErrorLabel.Text = message;
            ErrorLabel.ForeColor = error ? Theme.Warning : Theme.Accent;
        }
    }
}
