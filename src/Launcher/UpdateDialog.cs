using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class UpdateDialog : EditorDialog
    {
        private readonly Label status;
        private readonly TextBox notes;
        private readonly Control notesViewport;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Func<bool> canInstall;
        private UpdateRelease release;
        private bool busy;

        internal UpdateDialog(Func<bool> canInstall, UpdateRelease knownRelease = null) : base(T("AO Nexus updates"), 520)
        {
            this.canInstall = canInstall;
            release = knownRelease;
            AddWrappedNote(F("Installed version: {0}", AppUpdates.CurrentVersion.ToString(3)), "installedVersion");
            AddWrappedNote(T("Updates come from RazRn/AO-Nexus on GitHub. Installation closes and reopens Nexus. Game windows and your saved profile are kept. Save any pending settings first."), "updateGuide");
            status = AddWrappedNote(T("Checking for updates…"), "updateStatus");
            notes = new NexusTextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Top, Height = 180, ScrollBars = ScrollBars.Vertical, TabStop = false,
                BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Foreground, Font = new Font("Segoe UI", 9F) };
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
                notes.Text = release.Notes; SaveButton.Text = T("Install and restart");
                notesViewport.Visible = true;
                ClientSize = new Size(ClientSize.Width, (int)(520 * DeviceDpi / 96.0));
            }
            SaveButton.Focus();
            Fields.AutoScrollPosition = Point.Empty;
        }
        private async Task Check()
        {
            busy = true; SaveButton.Enabled = false; ErrorLabel.Text = ""; status.Text = T("Checking for updates…");
            try
            {
                var latest = await AppUpdates.CheckAsync(cancellation.Token);
                if (IsDisposed) return;
                release = latest; ShowRelease();
            }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) { ErrorLabel.Text = F("Could not check for updates: {0}", ex.Message); SaveButton.Text = T("Check again"); } }
            finally { busy = false; if (!IsDisposed) SaveButton.Enabled = true; }
        }
        private async Task Install()
        {
            if (!canInstall()) { ErrorLabel.Text = T("Finish or stop current launcher activity before installing an update."); return; }
            busy = true; SaveButton.Enabled = false; ErrorLabel.Text = "";
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var progress = new Progress<int>(percent => { if (!IsDisposed) status.Text = F("Downloading and verifying update… {0}%", percent); });
                    string staged = await AppUpdates.StageAsync(release, Application.ExecutablePath, timeout.Token, progress);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (IsDisposed) return;
                    if (!canInstall()) throw new InvalidOperationException(T("Finish or stop current launcher activity before installing an update."));
                    UpdateInstaller.Start(staged, Application.ExecutablePath);
                    Application.Exit();
                }
            }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) ErrorLabel.Text = F("Could not install update: {0}", ex.Message); }
            finally { busy = false; if (!IsDisposed) SaveButton.Enabled = true; }
        }
        protected override void Dispose(bool disposing) { if (disposing && !IsDisposed) { cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
    }
}
