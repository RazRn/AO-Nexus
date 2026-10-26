using System.Drawing;
using System.Windows.Forms;

namespace AOLauncher
{
    internal class NexusForm : Form
    {
        protected NexusForm()
        {
            // Set the surface before derived constructors can create native handles.
            BackColor = Theme.Background;
            ForeColor = Theme.Foreground;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Form.DoubleBuffered alone excludes child HWNDs (edits, grids,
                // panels). Present their paint together instead of exposing each
                // child's background erase while the rest of the dialog catches up.
                parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return parameters;
            }
        }
    }
}
