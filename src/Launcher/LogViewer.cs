using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class LogViewer : Form
    {
        internal static readonly Color TerminalBackground = Color.FromArgb(7, 12, 10);
        internal static readonly Color TerminalForeground = Color.FromArgb(151, 211, 166);
        private readonly TextBox search = new NexusTextBox { Dock = DockStyle.Fill, MaxLength = 512, AutoSize = false };
        private readonly TextBox output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None, BackColor = TerminalBackground,
            ForeColor = TerminalForeground, Font = new Font("Consolas", 10F) };
        private readonly CheckBox follow = new NexusCheckBox { AutoSize = true, Checked = true };
        private readonly Label count = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };
        private readonly Timer refresh = new Timer { Interval = 150 };
        private string source = "";
        private string lastSearch = "";

        internal LogViewer()
        {
            Text = T("Activity log");
            AppArtwork.ApplyIcon(this);
            Font = new Font("Segoe UI", 10F);
            BackColor = Theme.Background; ForeColor = Theme.Foreground;
            ClientSize = new Size(900, 500); MinimumSize = new Size(600, 320);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 3, RowCount = 1 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(new Label { Text = T("Search"), AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
            search.AccessibleName = T("Search log"); search.Margin = new Padding(0, 1, 8, 8);
            toolbar.Controls.Add(search, 1, 0);
            var clear = Theme.Button(T("Clear search")); clear.Margin = new Padding(0, 1, 0, 8);
            search.Height = clear.Height;
            clear.Click += (sender, e) => { search.Clear(); search.Focus(); };
            toolbar.Controls.Add(clear, 2, 0);
            var frame = new NexusPanel { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = Padding.Empty, BackColor = TerminalBackground };
            output.AccessibleName = T("Activity log");
            frame.Controls.Add(SlimScrolling.Wrap(output));
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            follow.Text = T("Follow new entries"); follow.Margin = new Padding(8, 6, 0, 0);
            footer.Controls.Add(count, 0, 0); footer.Controls.Add(follow, 1, 0);
            layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(frame, 0, 1); layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            search.TextChanged += (sender, e) => QueueRefresh();
            follow.CheckedChanged += (sender, e) => QueueRefresh();
            refresh.Tick += (sender, e) => RefreshLog();
            Shown += (sender, e) => { RefreshLog(); search.Focus(); };
        }

        internal void SetLogText(string text) { source = text ?? ""; QueueRefresh(); }
        internal void RefreshLanguage() { NexusStrings.Retranslate(this); RefreshLog(); }
        private void QueueRefresh() { if (!refresh.Enabled) refresh.Start(); }
        private void RefreshLog()
        {
            refresh.Stop();
            string[] all = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string term = search.Text;
            var matches = all.Where(line => term.Length == 0 || line.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            string text = string.Join(Environment.NewLine, matches);
            int start = output.SelectionStart, length = output.SelectionLength;
            int firstLine = term == lastSearch ? SendMessage(output.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0;
            lastSearch = term;
            if (output.Text != text) output.Text = text;
            if (follow.Checked) { output.SelectionStart = output.TextLength; output.SelectionLength = 0; output.ScrollToCaret(); }
            else
            {
                output.Select(Math.Min(start, output.TextLength), Math.Min(length, Math.Max(0, output.TextLength - start)));
                int currentLine = SendMessage(output.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                SendMessage(output.Handle, 0x00B6, IntPtr.Zero, new IntPtr(firstLine - currentLine));
            }
            count.Text = F("{0} of {1} log entries", matches.Length, all.Length);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
