using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using static AOLauncher.SidebarText;

namespace AOLauncher
{
    internal sealed class ComponentDownloadDialog : NexusForm
    {
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        bool finished;
        internal string InstalledFolder { get; private set; }
        ComponentDownloadDialog(string id)
        {
            Text = id == "aovk" ? "AO-VK" : "AO#";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(500, 170); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; BackColor = Theme.Background;
            var status = new Label { Text = AOLauncher.Localization.Texts.Get("Text_Downloading_from_RazRn_AO_Nexus_on_1BFA7E40B132"), ForeColor = Theme.Foreground,
                Bounds = new Rectangle(18, 15, 464, 64), UseMnemonic = false };
            var progress = new ProgressBar { Bounds = new Rectangle(18, 82, 464, 18) };
            var cancel = Theme.Button(AOLauncher.Localization.Texts.Get("Text_Cancel_34A3CBFEA4A5")); cancel.SetBounds(362, 115, 120, 34);
            cancel.Click += (sender, e) => { if (finished) Close(); else cancellation.Cancel(); };
            Controls.AddRange(new Control[] { status, progress, cancel });
            FormClosing += (sender, e) => { if (!finished) { cancellation.Cancel(); e.Cancel = true; status.Text = AOLauncher.Localization.Texts.Get("Text_Cancelling_613FF65C951B"); } };
            Shown += async (sender, e) =>
            {
                try
                {
                    var updates = new Progress<int>(value =>
                    {
                        progress.Value = value;
                        status.Text = value >= 90 ? AOLauncher.Localization.Texts.Get("Text_Verifying_and_installing_0F50279D0A97") : AOLauncher.Localization.Texts.Get("Text_Downloading_4716276E68E3") + value + "%";
                    });
                    InstalledFolder = await System.Threading.Tasks.Task.Run(() => OptionalComponents.InstallAsync(id, cancellation.Token, updates));
                    finished = true; DialogResult = DialogResult.OK; Close();
                }
                catch (OperationCanceledException) { finished = true; DialogResult = DialogResult.Cancel; Close(); }
                catch (Exception ex) { finished = true; status.Text = ex.Message; status.ForeColor = Theme.Warning; cancel.Text = AOLauncher.Localization.Texts.Get("Text_Close_B182D0987175"); }
            };
        }
        internal static string Download(IWin32Window owner, string id)
        {
            using (var dialog = new ComponentDownloadDialog(id))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.InstalledFolder : null;
        }
        protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
    }
}
