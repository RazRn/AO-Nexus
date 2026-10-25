using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    // Remote release text is displayed as text, never HTML or supplied RTF.
    internal sealed class ReleaseNotesBox : RichTextBox
    {
        internal ReleaseNotesBox()
        {
            ReadOnly = true; DetectUrls = false; BorderStyle = BorderStyle.None;
            Dock = DockStyle.Top; Height = 270; ScrollBars = RichTextBoxScrollBars.Vertical;
            TabStop = false; BackColor = Theme.Surface; ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 10F); Name = "releaseNotes";
        }
        internal void SetMarkdown(string markdown)
        {
            markdown = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            if (markdown.Length > 32000) markdown = markdown.Substring(0, 32000);
            Text = AppUpdates.FormatReleaseNotes(markdown);
            SelectAll(); SelectionFont = Font; SelectionColor = Theme.Foreground;
            var headings = new System.Collections.Generic.HashSet<string>(System.Text.RegularExpressions.Regex.Matches(markdown, @"(?m)^#{1,6}[ \t]+(.+)$")
                .Cast<System.Text.RegularExpressions.Match>().Select(match => AppUpdates.FormatReleaseNotes(match.Groups[1].Value)));
            var lines = Lines;
            using (var headingFont = new Font(Font, FontStyle.Bold))
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i]; Select(GetFirstCharIndexFromLine(i), line.Length);
                    if (headings.Contains(line)) { SelectionFont = headingFont; SelectionColor = Theme.Accent; }
                }
            }
            Select(0, 0); ScrollToCaret();
        }
    }
    internal sealed class WhatsNewDialog : EditorDialog
    {
        internal WhatsNewDialog() : base(F("What's new in AO Nexus {0}", AppUpdates.CurrentVersion.ToString(3)), 520)
        {
            AddWrappedNote(T("Update installed successfully. Your accounts, passwords and team presets are preserved."), "updateSuccess");
            var notes = new NexusTextBox { Name = "installedReleaseNotes", ReadOnly = true, Multiline = true,
                Dock = DockStyle.Top, Height = 300, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, TabStop = false,
                BackColor = Theme.Surface, ForeColor = Theme.Foreground, Font = new Font("Segoe UI", 10F) };
            string language = CurrentLanguage == "da" ? "da" : "en";
            using (var stream = typeof(WhatsNewDialog).Assembly.GetManifestResourceStream("AOLauncher.WhatsNew." + language + ".txt"))
            using (var reader = new System.IO.StreamReader(stream)) notes.Text = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n");
            Fields.Controls.Add(SlimScrolling.Wrap(notes));
            CancelAction.Text = T("Close");
            CancelAction.Click += (sender, e) => Close();
            SaveButton.Text = T("View release on GitHub");
            SaveButton.Click += (sender, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppUpdates.Repository + "/releases/tag/v" + AppUpdates.CurrentVersion.ToString(3)) { UseShellExecute = true }); }
                catch (Exception ex) { ErrorLabel.Text = ex.Message; }
            };
            AcceptButton = CancelAction;
            ActiveControl = CancelAction;
            Shown += (sender, e) => { CancelAction.Focus(); notes.Select(0, 0); Fields.AutoScrollPosition = Point.Empty; };
        }
    }

    internal sealed class UpdateDialog : EditorDialog
    {
        private readonly Label status;
        private readonly ReleaseNotesBox notes;
        private readonly Control notesViewport;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Func<bool> canInstall;
        private UpdateRelease release;
        private bool busy;
        internal Action<string> LogAction;
        private void Record(string message) { UpdateJournal.Record(message); LogAction?.Invoke(message); }

        internal UpdateDialog(Func<bool> canInstall, UpdateRelease knownRelease = null) : base(T("AO Nexus updates"), 520)
        {
            this.canInstall = canInstall;
            release = knownRelease;
            AddWrappedNote(F("Installed version: {0}", AppUpdates.CurrentVersion.ToString(3)), "installedVersion");
            AddWrappedNote(T("Updates come from RazRn/AO-Nexus on GitHub. Installation closes and reopens Nexus. Game windows and your saved profile are kept. Save any pending settings first."), "updateGuide");
            status = AddWrappedNote(T("Checking for updates…"), "updateStatus");
            ClientSize = new Size(620, 600);
            notes = new ReleaseNotesBox();
            notesViewport = SlimScrolling.Wrap(notes);
            Fields.Controls.Add(notesViewport);
            SaveButton.Click += async (sender, e) => { if (!busy) { if (release == null) await Check(); else await Install(); } };
            Shown += async (sender, e) => { if (release == null) await Check(); else ShowRelease(); };
            FormClosing += (sender, e) => cancellation.Cancel();
        }
        private void ShowRelease()
        {
            if (release.Version <= AppUpdates.CurrentVersion)
            {
                status.Text = T("You are using the latest version."); release = null; SaveButton.Text = T("Check again");
                notesViewport.Visible = false;
                ClientSize = new Size(ClientSize.Width, (int)(320 * DeviceDpi / 96.0));
            }
            else
            {
                status.Text = F("Version {0} is available.", release.Version.ToString(3));
                notes.BorderStyle = BorderStyle.None;
                notes.SetMarkdown(release.Notes); SaveButton.Text = T("Install and restart");
                notesViewport.Visible = true;
                ClientSize = new Size(ClientSize.Width, (int)(600 * DeviceDpi / 96.0));
            }
            SaveButton.Focus();
            Fields.AutoScrollPosition = Point.Empty;
        }
        private async Task Check()
        {
            Record(T("Checking for updates…"));
            busy = true; SaveButton.Enabled = false; ErrorLabel.Text = ""; status.Text = T("Checking for updates…");
            try
            {
                var latest = await AppUpdates.CheckAsync(cancellation.Token);
                if (IsDisposed) return;
                release = latest; ShowRelease();
                Record(status.Text);
            }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) { ErrorLabel.Text = F("Could not check for updates: {0}", ex.Message); Record(ErrorLabel.Text); SaveButton.Text = T("Check again"); } }
            finally { busy = false; if (!IsDisposed) SaveButton.Enabled = true; }
        }
        private async Task Install()
        {
            if (!canInstall()) { ErrorLabel.Text = T("Finish or stop current launcher activity before installing an update."); return; }
            busy = true; SaveButton.Enabled = false; ErrorLabel.Text = "";
            Record(F("Downloading update {0} from GitHub.", release.Version.ToString(3)));
            string staged = null;
            bool handedOff = false;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var progress = new Progress<int>(percent => { if (!IsDisposed) status.Text = F("Downloading and verifying update… {0}%", percent); });
                    staged = await AppUpdates.StageAsync(release, Application.ExecutablePath, timeout.Token, progress);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (IsDisposed) return;
                    if (!canInstall()) throw new InvalidOperationException(T("Finish or stop current launcher activity before installing an update."));
                    Record(T("Update verified. Restarting Nexus to install it."));
                    UpdateInstaller.Start(staged, Application.ExecutablePath);
                    handedOff = true;
                    Application.Exit();
                }
            }
            catch (Exception ex)
            {
                if (cancellation.IsCancellationRequested) UpdateJournal.Record("Update download cancelled.");
                else { string error = F("Could not install update: {0}", ex.Message); Record(error); if (!IsDisposed) ErrorLabel.Text = error; }
            }
            finally
            {
                if (!handedOff && staged != null) UpdateCleanup.RemoveStage(AppUpdates.UpdateRoot, System.IO.Path.GetDirectoryName(staged));
                busy = false; if (!IsDisposed) SaveButton.Enabled = true;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
    }
}
