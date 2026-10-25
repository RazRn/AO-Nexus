using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class HotkeyCapture : Button
    {
        internal HotkeyBinding Binding;
        internal bool Capturing { get; private set; }
        internal Action CaptureChanged;
        // Return an error to leave the previous binding unchanged.
        internal Func<HotkeyBinding, string> Commit;
        internal Action<string> Error;
        internal HotkeyCapture()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderColor = Theme.Border;
            BackColor = Theme.SurfaceRaised; ForeColor = Theme.Foreground;
            Font = new Font("Segoe UI", 9F); Height = 30;
            AccessibleRole = AccessibleRole.HotkeyField;
        }
        internal void RefreshText() { Text = Capturing ? T("Press a shortcut…") : Binding?.ToString() ?? T("Click to set"); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Capturing = true; RefreshText(); CaptureChanged?.Invoke(); }
        protected override void OnLeave(EventArgs e) { StopCapture(); base.OnLeave(e); }
        private void StopCapture() { if (!Capturing) return; Capturing = false; RefreshText(); CaptureChanged?.Invoke(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Capturing) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { StopCapture(); return true; }
            if (key == Keys.Tab) { StopCapture(); return base.ProcessCmdKey(ref msg, keyData); }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            HotkeyBinding candidate = null;
            if (key != Keys.Back && key != Keys.Delete)
            {
                candidate = new HotkeyBinding { Key = (int)key,
                    Modifiers = ((keyData & Keys.Control) != 0 ? 2u : 0) | ((keyData & Keys.Alt) != 0 ? 1u : 0) | ((keyData & Keys.Shift) != 0 ? 4u : 0) };
                if (!candidate.IsValid || (GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0)
                { Error?.Invoke(T("Use Ctrl or Alt with a letter, number or function key (except F12).")); return true; }
            }
            string error = Commit?.Invoke(candidate);
            if (error != null) { Error?.Invoke(error); return true; }
            Binding = candidate; StopCapture(); return true;
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }
}
