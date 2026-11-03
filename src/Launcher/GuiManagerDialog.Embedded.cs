using System.Drawing;
using System.Windows.Forms;

namespace AOLauncher
{
    // Lets the Game GUI tools live as Layouts / Skins / Scripts / Backups tabs in the main window.
    internal sealed partial class GuiManagerDialog
    {
        internal bool IsBusy => busy;

        // The main window's tab row replaces this dialog's own tab headers, which are clipped away.
        internal void PrepareEmbedded()
        {
            CancelAction.Visible = false;
            var clip = new Panel { Name = "gameGuiPageClip", Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Background };
            DialogLayout.Controls.Remove(tabs);
            DialogLayout.Controls.Add(clip, 0, 0);
            tabs.Dock = DockStyle.None;
            clip.Controls.Add(tabs);
            void Fit()
            {
                int header = tabs.TabCount > 0 && tabs.IsHandleCreated ? tabs.GetTabRect(0).Bottom + 2 : tabs.ItemSize.Height + 6;
                tabs.SetBounds(0, -header, clip.ClientSize.Width, clip.ClientSize.Height + header);
            }
            clip.Resize += (sender, e) => Fit();
            tabs.HandleCreated += (sender, e) => Fit();
            Fit();
        }

        internal void ShowTab(int index)
        {
            if (index >= 0 && index < tabs.TabCount) tabs.SelectedIndex = index;
        }
    }
}
