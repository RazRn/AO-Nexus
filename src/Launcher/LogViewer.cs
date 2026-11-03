using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static AOLauncher.Localization.Texts;

namespace AOLauncher
{
    internal sealed class LogViewer : NexusForm
    {
        internal static readonly Color TerminalBackground = Color.FromArgb(7, 12, 10);
        internal static readonly Color TerminalForeground = Color.FromArgb(151, 211, 166);
        private readonly TextBox search = new NexusTextBox { Dock = DockStyle.Fill, MaxLength = 512, AutoSize = false };
        private readonly RichTextBox output = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.None, BackColor = TerminalBackground,
            ForeColor = TerminalForeground, Font = new Font("Consolas", 10F) };
        private readonly CheckBox follow = new NexusCheckBox { AutoSize = false, Size = new Size(165, 28), Checked = true };
        private readonly CheckBox wrap = new NexusCheckBox { Name = "wrapLogLines", AutoSize = false, Size = new Size(132, 34) };
        internal bool WrapLines { get => wrap.Checked; set => wrap.Checked = value; }
        internal Action<bool> WrapLinesChanged;
        private readonly Label count = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted };
        private readonly Timer refresh = new Timer { Interval = 150 };
        private string source = "";
        private string lastSearch = "";
        private string rendered;
        private readonly Button clearLog = Theme.Button(T("Clear log…"));
        internal Action ClearLogRequested;
        internal Action ReportProblemRequested;
        private readonly Button reportProblem = Theme.Button(T("Report a problem…"));
        internal Func<bool> ConfirmClearLog;

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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4, RowCount = 1 };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.Controls.Add(new Label { Text = T("Search"), AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
            search.AccessibleName = T("Search log"); search.Margin = new Padding(0, 1, 8, 8);
            toolbar.Controls.Add(search, 1, 0);
            var clear = Theme.Button(T("Clear search")); clear.Margin = new Padding(0, 1, 0, 8);
            search.Height = clear.Height;
            clear.Click += (sender, e) => { search.Clear(); search.Focus(); };
            toolbar.Controls.Add(clear, 2, 0);
            wrap.Text = T("Wrap lines"); wrap.Margin = new Padding(12, 1, 0, 8);
            wrap.CheckedChanged += (sender, e) => ApplyWrap();
            toolbar.Controls.Add(wrap, 3, 0);
            var frame = new NexusPanel { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = Padding.Empty, BackColor = TerminalBackground };
            output.AccessibleName = T("Activity log");
            frame.Controls.Add(SlimScrolling.Wrap(output));
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            follow.Text = T("Follow new entries"); follow.Margin = new Padding(8, 6, 0, 0);
            footer.Controls.Add(count, 0, 0); footer.Controls.Add(follow, 1, 0);
            clearLog.Name = "clearActivityLog"; clearLog.Margin = new Padding(12, 6, 0, 0); clearLog.Enabled = false;
            clearLog.Click += (sender, e) => ClearActivity(); footer.Controls.Add(clearLog, 2, 0);
            reportProblem.Name = "reportProblemFromLog"; reportProblem.Margin = new Padding(8, 6, 0, 0); reportProblem.UseMnemonic = false;
            reportProblem.Click += (sender, e) => ReportProblemRequested?.Invoke(); footer.Controls.Add(reportProblem, 3, 0);
            layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(frame, 0, 1); layout.Controls.Add(footer, 0, 2);
            Controls.Add(layout);
            search.TextChanged += (sender, e) => QueueRefresh();
            follow.CheckedChanged += (sender, e) => QueueRefresh();
            refresh.Tick += (sender, e) => RefreshLog();
            Shown += (sender, e) => { RefreshLog(); search.Focus(); };
        }

        internal void SetLogText(string text) { source = text ?? ""; QueueRefresh(); }
        private void ApplyWrap()
        {
            int start = output.SelectionStart, length = output.SelectionLength;
            int anchor = output.IsHandleCreated ? output.GetCharIndexFromPosition(Point.Empty) : 0;
            output.WordWrap = wrap.Checked;
            output.ScrollBars = wrap.Checked ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
            if (output.IsHandleCreated) {
                if (follow.Checked) { output.Select(output.TextLength, 0); output.ScrollToCaret(); }
                else {
                    output.Select(Math.Min(start, output.TextLength), Math.Min(length, Math.Max(0, output.TextLength - start)));
                    int firstLine = SendMessage(output.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                    int wantedLine = output.GetLineFromCharIndex(Math.Min(anchor, output.TextLength));
                    SendMessage(output.Handle, 0x00B6, IntPtr.Zero, new IntPtr(wantedLine - firstLine));
                }
                SlimScrolling.LayoutNow(output);
            }
            WrapLinesChanged?.Invoke(wrap.Checked);
        }
        private void ClearActivity()
        {
            if (source.Length == 0) return;
            bool confirmed = ConfirmClearLog != null ? ConfirmClearLog() : NexusMessageBox.Show(this,
                T("Clear all activity log entries for this session, including entries hidden by the search? This cannot be undone. New activity will continue to appear. The separate update-history file is kept."),
                T("Clear activity log?"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.OK;
            if (!confirmed) return;
            ClearLogRequested?.Invoke();
            source = ""; search.Clear(); RefreshLog();
        }
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
            // Lines are coloured green (worked), yellow (skipped or needs a look) and red (failed).
            if (rendered != text) { rendered = text; LogTone.Show(output, text, 10F, false); }
            if (follow.Checked) { output.SelectionStart = output.TextLength; output.SelectionLength = 0; output.ScrollToCaret(); }
            else
            {
                output.Select(Math.Min(start, output.TextLength), Math.Min(length, Math.Max(0, output.TextLength - start)));
                int currentLine = SendMessage(output.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                SendMessage(output.Handle, 0x00B6, IntPtr.Zero, new IntPtr(firstLine - currentLine));
            }
            count.Text = F("{0} of {1} log entries", matches.Length, all.Length);
            clearLog.Enabled = source.Length > 0;
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
