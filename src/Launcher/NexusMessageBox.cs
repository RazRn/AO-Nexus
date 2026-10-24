using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AOLauncher.Localization;

namespace AOLauncher
{
    internal static class NexusMessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        internal static DialogResult Show(IWin32Window owner, string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            using (var dialog = new NexusMessageDialog(text, caption, buttons, icon, defaultButton))
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }
    }

    internal sealed class NexusMessageDialog : Form
    {
        private readonly List<Button> actionButtons = new List<Button>();
        private readonly Button defaultAction;
        internal TextBox MessageControl { get; }
        internal IReadOnlyList<Button> ActionButtons => actionButtons.AsReadOnly();
        internal DialogResult DefaultResult => defaultAction.DialogResult;
        internal DialogResult DismissResult { get; }

        internal NexusMessageDialog(string text, string caption, MessageBoxButtons buttons,
            MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            DialogResult[] results = ResultsFor(buttons);
            int requestedDefault = (int)defaultButton;
            if (requestedDefault != 0 && requestedDefault != 256 && requestedDefault != 512)
                throw new InvalidEnumArgumentException(nameof(defaultButton), requestedDefault, typeof(MessageBoxDefaultButton));
            int defaultIndex = requestedDefault / 256;
            if (defaultIndex >= results.Length) defaultIndex = 0;
            if (icon != MessageBoxIcon.None && icon != MessageBoxIcon.Error && icon != MessageBoxIcon.Question
                && icon != MessageBoxIcon.Warning && icon != MessageBoxIcon.Information)
                throw new InvalidEnumArgumentException(nameof(icon), (int)icon, typeof(MessageBoxIcon));

            DismissResult = Array.IndexOf(results, DialogResult.Cancel) >= 0 ? DialogResult.Cancel
                : buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.None;
            Name = "nexusMessageDialog";
            Text = caption ?? string.Empty;
            AccessibleName = Text;
            AccessibleRole = AccessibleRole.Dialog;
            Font = new Font("Segoe UI", 10F);
            BackColor = Theme.Background;
            ForeColor = Theme.Foreground;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = DismissResult != DialogResult.None;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AppArtwork.ApplyIcon(this);

            const int dialogWidth = 590;
            Size measured = TextRenderer.MeasureText(text ?? string.Empty, Font, new Size(dialogWidth - 88, 100000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            int messageHeight = Math.Max(78, Math.Min(330, measured.Height + 22));
            ClientSize = new Size(dialogWidth, messageHeight + 170);

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(24), Margin = Padding.Empty, BackColor = Theme.Background
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty,
                BackColor = Theme.Background, Padding = new Padding(0, 0, 0, 12)
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, icon == MessageBoxIcon.None ? 0 : 48));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (icon != MessageBoxIcon.None)
                heading.Controls.Add(new MessageGlyph(icon) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) }, 0, 0);
            heading.Controls.Add(new Label {
                Name = "messageCaption", Text = Text, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold), ForeColor = Theme.Foreground,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true
            }, 1, 0);

            MessageControl = new TextBox {
                Name = "messageText", Text = text ?? string.Empty, Dock = DockStyle.Fill,
                Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Foreground,
                Margin = Padding.Empty, TabIndex = 0, TabStop = true,
                AccessibleName = Text, AcceptsReturn = false, AcceptsTab = false
            };
            var messageSurface = new NexusPanel {
                Dock = DockStyle.Fill, Padding = new Padding(14), Margin = Padding.Empty, TabIndex = 0
            };
            messageSurface.Controls.Add(SlimScrolling.Wrap(MessageControl));

            var actions = new FlowLayoutPanel {
                Name = "messageActions", Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Padding = new Padding(0, 18, 0, 0), Margin = Padding.Empty, BackColor = Theme.Background, TabIndex = 1
            };
            for (int index = 0; index < results.Length; ++index)
            {
                DialogResult result = results[index];
                var button = Theme.Button(ButtonText(result), index == defaultIndex);
                button.Name = "messageAction" + result;
                button.DialogResult = result;
                button.TabIndex = index;
                button.Margin = new Padding(index == 0 ? 0 : 8, 0, 0, 0);
                actionButtons.Add(button);
            }
            // Right-to-left placement aligns the group to the edge while preserving
            // the standard left-to-right button order and explicit tab indices.
            for (int index = actionButtons.Count - 1; index >= 0; --index) actions.Controls.Add(actionButtons[index]);
            defaultAction = actionButtons[defaultIndex];
            AcceptButton = defaultAction;
            foreach (Button button in actionButtons)
                if (button.DialogResult == DismissResult) CancelButton = button;

            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(messageSurface, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout);
        }

        protected override void OnLoad(EventArgs e)
        {
            Theme.Apply(this);
            // Theme.Apply sets the general textbox border; message text already has
            // its own inset panel and should not gain an additional input outline.
            MessageControl.BorderStyle = BorderStyle.None;
            base.OnLoad(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            MessageControl.SelectionStart = 0;
            MessageControl.SelectionLength = 0;
            ActiveControl = defaultAction;
            defaultAction.Focus();
        }

        internal bool TryDismiss()
        {
            if (DismissResult == DialogResult.None) return false;
            DialogResult = DismissResult;
            Close();
            return true;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape && (keyData & (Keys.Alt | Keys.Control)) == 0)
            {
                TryDismiss();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                bool selectedAction = false;
                foreach (Button button in actionButtons)
                    if (button.DialogResult == DialogResult) selectedAction = true;
                // Form's native close path may assign Cancel before this event,
                // even when the message box has no Cancel action.
                if (!selectedAction)
                {
                    if (DismissResult == DialogResult.None)
                    {
                        DialogResult = DialogResult.None;
                        e.Cancel = true;
                    }
                    else DialogResult = DismissResult;
                }
            }
            base.OnFormClosing(e);
        }

        private static DialogResult[] ResultsFor(MessageBoxButtons buttons)
        {
            switch (buttons)
            {
                case MessageBoxButtons.OK: return new[] { DialogResult.OK };
                case MessageBoxButtons.OKCancel: return new[] { DialogResult.OK, DialogResult.Cancel };
                case MessageBoxButtons.AbortRetryIgnore: return new[] { DialogResult.Abort, DialogResult.Retry, DialogResult.Ignore };
                case MessageBoxButtons.YesNoCancel: return new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel };
                case MessageBoxButtons.YesNo: return new[] { DialogResult.Yes, DialogResult.No };
                case MessageBoxButtons.RetryCancel: return new[] { DialogResult.Retry, DialogResult.Cancel };
                default: throw new InvalidEnumArgumentException(nameof(buttons), (int)buttons, typeof(MessageBoxButtons));
            }
        }

        private static string ButtonText(DialogResult result)
        {
            bool danish = Texts.CurrentLanguage == "da";
            switch (result)
            {
                case DialogResult.OK: return "OK";
                case DialogResult.Cancel: return danish ? "Annuller" : "Cancel";
                case DialogResult.Yes: return danish ? "Ja" : "Yes";
                case DialogResult.No: return danish ? "Nej" : "No";
                case DialogResult.Abort: return danish ? "Afbryd" : "Abort";
                case DialogResult.Retry: return danish ? "Prøv igen" : "Retry";
                case DialogResult.Ignore: return danish ? "Ignorer" : "Ignore";
                default: return result.ToString();
            }
        }

        private sealed class MessageGlyph : Control
        {
            private readonly string symbol;
            private readonly Color color;
            internal MessageGlyph(MessageBoxIcon icon)
            {
                DoubleBuffered = true;
                TabStop = false;
                BackColor = Theme.Background;
                symbol = icon == MessageBoxIcon.Question ? "?" : icon == MessageBoxIcon.Error ? "×"
                    : icon == MessageBoxIcon.Warning ? "!" : "i";
                color = icon == MessageBoxIcon.Error ? Theme.Warning : icon == MessageBoxIcon.Warning ? Theme.Amber : Theme.Accent;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int diameter = Math.Max(1, Math.Min(Width, Height) - 4);
                var circle = new Rectangle((Width - diameter) / 2, (Height - diameter) / 2, diameter, diameter);
                using (var pen = new Pen(color, Math.Max(1F, DeviceDpi / 64F))) e.Graphics.DrawEllipse(pen, circle);
                using (var font = new Font("Segoe UI", 15F * DeviceDpi / 96F, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics, symbol, font, circle, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
